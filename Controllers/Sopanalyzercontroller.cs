using AgenticPitstop.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using System.Data;

namespace AgenticPitstop.Controllers;

/// <summary>
/// SOP Analyzer. A developer uploads a Standard Operating Procedure and gets
/// back the automation footprint and the exact human-in-the-loop checkpoints,
/// instead of reading the document for an afternoon.
///
/// Admin and Manager only: the blueprint drives how a use case will be built,
/// so it belongs with the people who design use cases rather than the people
/// who review records.
/// </summary>
[ApsAuthorize(ApsIdentity.RoleAdmin, ApsIdentity.RoleManager)]
public sealed class SopAnalyzerController : ApsController
{
    private readonly SopAnalyzerService _sop;

    public SopAnalyzerController(ApsDb db, ApsIdentity identity, ApsMetadata meta,
                                 SopAnalyzerService sop)
        : base(db, identity, meta)
    {
        _sop = sop;
    }

    protected override string ActiveScreenKey => "sop-analyzer";

    // =========================================================================
    // Screen
    // =========================================================================

    [HttpGet]
    public IActionResult Index(int useCaseId = 0)
    {
        var useCases = LoadUseCases();
        var allowed = useCases.AsEnumerable()
            .Select(r => ToInt(r, "UseCaseID"))
            .Where(id => id > 0)
            .ToHashSet();

        if (useCaseId == 0 && allowed.Count == 1) useCaseId = allowed.First();
        if (useCaseId > 0 && !allowed.Contains(useCaseId)) useCaseId = 0;

        ViewBag.Title = "SOP Analyzer";
        ViewBag.UseCases = useCases;
        ViewBag.UseCaseId = useCaseId;
        ViewBag.Clients = Db.Query("APS_ClientMaster_Get");

        // Named on the screen, so nobody mistakes a sample blueprint for a real
        // analysis of their document.
        ViewBag.AgentName = _sop.Agent.Name;
        ViewBag.IsSampleAgent = _sop.IsSample;

        ViewBag.History = useCaseId > 0 ? _sop.History(useCaseId) : new DataTable();

        return View();
    }

    // =========================================================================
    // Analyze
    // =========================================================================

    [HttpPost]
    [RequestSizeLimit(SopAnalyzerService.MaxUploadBytes)]
    public async Task<IActionResult> Analyze(IFormFile file, int useCaseId,
                                             string customer, string processName, string depth)
    {
        if (useCaseId <= 0) return Fail("Select a use case first.");
        if (!IsAllowedUseCase(useCaseId)) return Fail("You do not have access to this use case.");

        var problem = SopAnalyzerService.Validate(file);
        if (problem != null) return Fail(problem);

        byte[] content;
        using (var buffer = new MemoryStream())
        {
            await file.CopyToAsync(buffer, HttpContext.RequestAborted);
            content = buffer.ToArray();
        }

        var documentName = Path.GetFileName(file.FileName);

        // A copy of the SOP is kept where the platform's other files live, so a
        // blueprint can always be checked against the document it came from.
        // A missing copy never stops the analysis - the blueprint is what the
        // developer came for.
        var stored = _sop.TryStore(documentName, content, useCaseId);

        var started = _sop.Start(useCaseId, documentName, stored,
                                 customer?.Trim(), processName?.Trim(), depth?.Trim(), ActionBy);

        if (started.Id <= 0)
            return Fail(string.IsNullOrWhiteSpace(started.Message)
                ? "The analysis could not be started."
                : started.Message);

        try
        {
            var result = await _sop.AnalyzeAsync(
                new SopAnalysisRequest(documentName, file.ContentType, content,
                                       customer?.Trim(), processName?.Trim(), depth?.Trim()),
                HttpContext.RequestAborted);

            if (result == null || result.Steps.Count == 0)
                throw new InvalidOperationException("No process steps could be identified in this document.");

            _sop.Complete(started.Id, result, ActionBy);

            return Ok($"{result.Steps.Count} process step(s) identified.", new
            {
                SopAnalysisId = started.Id,
                IsSample = _sop.IsSample,
                AgentName = _sop.Agent.Name,
                Result = result
            });
        }
        catch (Exception ex)
        {
            // The row is marked failed so the history shows the attempt rather
            // than leaving it stuck at Running for ever.
            _sop.Fail(started.Id, ex.Message, ActionBy);
            return Fail($"The SOP could not be analyzed: {ex.Message}");
        }
    }

    // =========================================================================
    // Read one blueprint back
    // =========================================================================

    [HttpGet]
    public IActionResult Result(int id)
    {
        if (id <= 0) return Fail("Select an analysis first.");

        var row = _sop.ById(id);
        if (row == null) return Fail("That analysis no longer exists.");

        var useCaseId = ToInt(row, "UseCaseID");
        if (!IsAllowedUseCase(useCaseId)) return Fail("You do not have access to this use case.");

        var status = ApsMetadata.Cell(row, "Status");
        if (!string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase))
        {
            var message = ApsMetadata.Cell(row, "Message");
            return Fail(string.IsNullOrWhiteSpace(message)
                ? $"That analysis is {status.ToLowerInvariant()}."
                : message);
        }

        SopAnalyzerResult result;
        try
        {
            result = SopAnalyzerResult.Parse(ApsMetadata.Cell(row, "ResultJson"));
        }
        catch (InvalidOperationException ex)
        {
            return Fail(ex.Message);
        }

        if (result == null) return Fail("That analysis has no stored blueprint.");

        result.Reconcile();

        return Ok(string.Empty, new
        {
            SopAnalysisId = id,
            DocumentName = ApsMetadata.Cell(row, "DocumentName"),
            CreatedOn = ApsMetadata.Cell(row, "CreatedDate"),
            Result = result
        });
    }

    // =========================================================================
    // Helpers - same shape as InflowController, so access is decided one way
    // =========================================================================

    private DataTable LoadUseCases()
    {
        var userName = Identity.AccessType == ApsIdentity.RoleAdmin ? null : ActionBy;
        return Db.Query("APS_UseCaseMaster_Get", p =>
        {
            ApsDb.In(p, "@ClientID", null);
            ApsDb.In(p, "@ProcessID", null);
            ApsDb.In(p, "@UserName", userName);
        });
    }

    private bool IsAllowedUseCase(int useCaseId)
    {
        if (Identity.AccessType == ApsIdentity.RoleAdmin) return true;

        var outputs = Db.ExecuteWithOutput("APS_UserUseCaseMapping_Check", p =>
        {
            ApsDb.In(p, "@UserName", ActionBy);
            ApsDb.In(p, "@UseCaseID", useCaseId);
            ApsDb.Out(p, "@IsAllowed", SqlDbType.Bit);
        });

        return outputs.TryGetValue("IsAllowed", out var allowed)
            && allowed != null
            && Convert.ToBoolean(allowed);
    }

    private static int ToInt(DataRow row, string column)
    {
        if (row == null || !row.Table.Columns.Contains(column)) return 0;
        return row[column] == DBNull.Value ? 0 : Convert.ToInt32(row[column]);
    }
}