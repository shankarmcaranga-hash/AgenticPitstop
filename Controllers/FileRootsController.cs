using System.Data;
using System.Text.RegularExpressions;
using AgenticPitstop.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace AgenticPitstop.Controllers;

/// <summary>
/// Folder Roots. Admin only: these decide where pipelines write documents.
/// A folder must sit inside Aps:AllowedBaseFolders, which is set per environment.
/// </summary>
[ApsAuthorize(ApsIdentity.RoleAdmin)]
public sealed class FileRootsController : ApsController
{
    private readonly ApsFileRoots _roots;

    public FileRootsController(ApsDb db, ApsIdentity identity, ApsMetadata meta, ApsFileRoots roots)
        : base(db, identity, meta) => _roots = roots;

    protected override string ActiveScreenKey => "file-roots";

    [HttpGet]
    public IActionResult Index()
    {
        ViewBag.Roots = Db.Query("APS_FileRoot_Get", p => ApsDb.In(p, "@ActiveOnly", 0));
        ViewBag.AllowedBases = _roots.AllowedBases();
        return View();
    }

    [HttpPost]
    public IActionResult Save(int fileRootId, string rootAlias, string rootPath, string description, bool isDefault)
    {
        rootAlias = rootAlias?.Trim();
        rootPath = rootPath?.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.IsNullOrEmpty(rootAlias) || !Regex.IsMatch(rootAlias, "^[A-Za-z][A-Za-z0-9 _-]{1,49}$"))
            return Fail("Name: 2-50 characters, starting with a letter.");

        if (string.IsNullOrEmpty(rootPath) || rootPath.Length > 400)
            return Fail("Enter the folder path.");

        if (!_roots.IsInsideAllowedBase(rootPath))
            return Fail("That folder is outside the areas this server allows. " +
                        "Choose a folder inside one of the permitted areas listed on this screen.");

        // Checked once here rather than during every run.
        try
        {
            Directory.CreateDirectory(rootPath);
            var probe = Path.Combine(rootPath, ".aps-write-test");
            System.IO.File.WriteAllText(probe, string.Empty);
            System.IO.File.Delete(probe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return Fail($"The folder could not be written to: {ex.Message}");
        }

        var outputs = Db.ExecuteWithOutput("APS_FileRoot_Save", p =>
        {
            ApsDb.In(p, "@FileRootID", fileRootId);
            ApsDb.In(p, "@RootAlias", rootAlias);
            ApsDb.In(p, "@RootPath", rootPath);
            ApsDb.In(p, "@Description", string.IsNullOrWhiteSpace(description) ? null : description.Trim());
            ApsDb.In(p, "@IsDefault", isDefault ? 1 : 0);
            ApsDb.In(p, "@ActionBy", ActionBy);
            ApsDb.Out(p, "@NewId", SqlDbType.Int);
            ApsDb.Out(p, "@Message", SqlDbType.VarChar, 300);
        });

        return FromProcedure(outputs, "NewId");
    }

    [HttpPost]
    public IActionResult ToggleStatus(int fileRootId)
    {
        var outputs = Db.ExecuteWithOutput("APS_FileRoot_ToggleStatus", p =>
        {
            ApsDb.In(p, "@FileRootID", fileRootId);
            ApsDb.In(p, "@ActionBy", ActionBy);
            ApsDb.Out(p, "@Success", SqlDbType.TinyInt);
            ApsDb.Out(p, "@Message", SqlDbType.VarChar, 300);
        });

        return FromProcedure(outputs);
    }
}