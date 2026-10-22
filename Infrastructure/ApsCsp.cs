using System.Security.Cryptography;

namespace AgenticPitstop.Infrastructure;

/// <summary>
/// Per-request CSP nonce.
///
/// The portal renders screen behaviour as inline script inside partials and
/// sections, so 'self' alone is not enough. A nonce keeps the policy strict —
/// only scripts carrying this request's value run — without falling back to
/// 'unsafe-inline', which would allow any injected script on the page.
/// </summary>
public static class ApsCsp
{
    private const string ItemsKey = "aps.csp.nonce";

    /// <summary>Generates the nonce once per request and caches it.</summary>
    public static string ApsNonce(this HttpContext context)
    {
        if (context.Items.TryGetValue(ItemsKey, out var existing) && existing is string cached)
            return cached;

        var bytes = RandomNumberGenerator.GetBytes(16);
        var nonce = Convert.ToBase64String(bytes);

        context.Items[ItemsKey] = nonce;
        return nonce;
    }
}