using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SyntaxCircus.RevenueCat;

/// <summary>Verifies RevenueCat's timestamped HMAC-SHA256 webhook signature over the exact bytes received.</summary>
public static class RevenueCatSignatureVerifier
{
    /// <summary>
    /// Validates a <c>X-RevenueCat-Webhook-Signature</c> value in the form
    /// <c>t=&lt;unix_timestamp&gt;,v1=&lt;hmac_sha256_hex&gt;</c>, per RevenueCat's documented webhook
    /// signing scheme (https://www.revenuecat.com/docs/integrations/webhooks): HMAC-SHA256 over
    /// <c>"{timestamp}.{rawBody}"</c>, with a caller-supplied tolerance window against
    /// <paramref name="now"/> to guard against replay.
    /// </summary>
    /// <param name="rawBody">The raw request body bytes, read before any JSON deserialization.</param>
    /// <param name="signatureHeader">The value of the <c>X-RevenueCat-Webhook-Signature</c> header.</param>
    /// <param name="secret">The webhook signing secret configured in the RevenueCat dashboard.</param>
    /// <param name="now">The current time, used to evaluate <paramref name="tolerance"/> against the signed timestamp.</param>
    /// <param name="tolerance">Maximum permitted clock skew between the signed timestamp and <paramref name="now"/>.</param>
    public static bool Verify(
        ReadOnlySpan<byte> rawBody,
        string signatureHeader,
        string secret,
        DateTimeOffset now,
        TimeSpan tolerance)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader)
            || string.IsNullOrWhiteSpace(secret)
            || tolerance < TimeSpan.Zero
            || !TryParseHeader(signatureHeader, out var timestamp, out var providedSignature))
        {
            return false;
        }

        DateTimeOffset signedAt;
        try
        {
            signedAt = DateTimeOffset.FromUnixTimeSeconds(timestamp);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        if ((now - signedAt).Duration() > tolerance)
        {
            return false;
        }

        var timestampPrefix = Encoding.ASCII.GetBytes($"{timestamp.ToString(CultureInfo.InvariantCulture)}.");
        var signedPayload = new byte[timestampPrefix.Length + rawBody.Length];
        timestampPrefix.CopyTo(signedPayload, 0);
        rawBody.CopyTo(signedPayload.AsSpan(timestampPrefix.Length));

        var computedSignature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), signedPayload);
        return CryptographicOperations.FixedTimeEquals(computedSignature, providedSignature);
    }

    private static bool TryParseHeader(string header, out long timestamp, out byte[] signature)
    {
        timestamp = default;
        signature = [];

        string? timestampValue = null;
        string? signatureValue = null;
        foreach (var part in header.Split(',', StringSplitOptions.TrimEntries))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0 || separator == part.Length - 1)
            {
                return false;
            }

            var name = part[..separator];
            var value = part[(separator + 1)..];
            if (name.Equals("t", StringComparison.Ordinal))
            {
                if (timestampValue is not null)
                {
                    return false;
                }

                timestampValue = value;
            }
            else if (name.Equals("v1", StringComparison.Ordinal))
            {
                if (signatureValue is not null)
                {
                    return false;
                }

                signatureValue = value;
            }
            else
            {
                return false;
            }
        }

        if (timestampValue is null
            || signatureValue is null
            || !long.TryParse(timestampValue, NumberStyles.None, CultureInfo.InvariantCulture, out timestamp)
            || timestamp < 0
            || signatureValue.Length != 64)
        {
            return false;
        }

        try
        {
            signature = Convert.FromHexString(signatureValue);
            return signature.Length == 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
