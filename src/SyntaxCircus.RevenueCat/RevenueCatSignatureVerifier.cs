using System.Security.Cryptography;
using System.Text;

namespace SyntaxCircus.RevenueCat;

/// <summary>Verifies HMAC-SHA256 signatures on inbound RevenueCat webhook payloads.</summary>
public static class RevenueCatSignatureVerifier
{
    /// <summary>
    /// Verifies the <c>X-RevenueCat-Signature</c> header against the raw request body.
    /// </summary>
    /// <param name="rawBody">The raw UTF-8 request body, read before any JSON deserialization.</param>
    /// <param name="signatureHeader">The value of the <c>X-RevenueCat-Signature</c> header.</param>
    /// <param name="secret">The webhook signing secret configured in the RevenueCat dashboard.</param>
    public static bool Verify(string rawBody, string signatureHeader, string secret)
    {
        ArgumentNullException.ThrowIfNull(rawBody);
        ArgumentNullException.ThrowIfNull(signatureHeader);
        ArgumentNullException.ThrowIfNull(secret);

        var key = Encoding.UTF8.GetBytes(secret);
        using var hmac = new HMACSHA256(key);
        var computed = hmac.ComputeHash(Encoding.UTF8.GetBytes(rawBody));
        var computedHex = Convert.ToHexString(computed).ToLowerInvariant();

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computedHex),
            Encoding.UTF8.GetBytes(signatureHeader.ToLowerInvariant()));
    }
}
