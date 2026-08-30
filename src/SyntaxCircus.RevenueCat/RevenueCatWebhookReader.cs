using System.Text;
using System.Text.Json;

namespace SyntaxCircus.RevenueCat;

public enum RevenueCatWebhookStatus
{
    Success,
    Unauthorized,
    Malformed,
    TooLarge,
}

public sealed record RevenueCatWebhookReadResult(
    RevenueCatWebhookStatus Status,
    RevenueCatWebhookPayload? Payload = null,
    string? RawBody = null)
{
    public static RevenueCatWebhookReadResult Unauthorized() => new(RevenueCatWebhookStatus.Unauthorized);

    public static RevenueCatWebhookReadResult Malformed() => new(RevenueCatWebhookStatus.Malformed);

    public static RevenueCatWebhookReadResult TooLarge() => new(RevenueCatWebhookStatus.TooLarge);

    public static RevenueCatWebhookReadResult Success(RevenueCatWebhookPayload payload, string rawBody)
        => new(RevenueCatWebhookStatus.Success, payload, rawBody);
}

/// <summary>
/// Reads and verifies an inbound RevenueCat webhook request: enforces a maximum body size, buffers
/// the raw body (so it can be both HMAC-verified and JSON-deserialized), verifies the
/// <c>X-RevenueCat-Webhook-Signature</c> header against <see cref="RevenueCatOptions.WebhookSecret"/>,
/// then deserializes the envelope. Storing the event for idempotency and dispatching it for
/// processing is left to the caller — this only answers "is this request genuinely from RevenueCat,
/// and what does it say".
/// </summary>
public static class RevenueCatWebhookReader
{
    private const string SignatureHeaderName = "X-RevenueCat-Webhook-Signature";

    public static async Task<RevenueCatWebhookReadResult> ReadAndVerifyAsync(
        HttpRequest request,
        RevenueCatOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);

        if (request.ContentLength is > 0 && request.ContentLength > options.WebhookMaxBodyBytes)
        {
            return RevenueCatWebhookReadResult.TooLarge();
        }

        byte[] rawBodyBytes;
        request.EnableBuffering(bufferLimit: options.WebhookMaxBodyBytes);
        try
        {
            using var buffer = new MemoryStream();
            await request.Body.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            rawBodyBytes = buffer.ToArray();
            request.Body.Position = 0;
        }
        catch (IOException)
        {
            return RevenueCatWebhookReadResult.TooLarge();
        }

        if (rawBodyBytes.Length > options.WebhookMaxBodyBytes)
        {
            return RevenueCatWebhookReadResult.TooLarge();
        }

        if (!string.IsNullOrEmpty(options.WebhookSecret))
        {
            var signature = request.Headers[SignatureHeaderName].FirstOrDefault();
            if (string.IsNullOrEmpty(signature)
                || !RevenueCatSignatureVerifier.Verify(
                    rawBodyBytes,
                    signature,
                    options.WebhookSecret,
                    DateTimeOffset.UtcNow,
                    TimeSpan.FromSeconds(options.WebhookSignatureToleranceSeconds)))
            {
                return RevenueCatWebhookReadResult.Unauthorized();
            }
        }
        else if (options.RequireWebhookSecret)
        {
            return RevenueCatWebhookReadResult.Unauthorized();
        }

        var rawBody = Encoding.UTF8.GetString(rawBodyBytes);

        RevenueCatWebhookPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<RevenueCatWebhookPayload>(rawBody);
        }
        catch (JsonException)
        {
            return RevenueCatWebhookReadResult.Malformed();
        }

        if (payload?.Event is null || string.IsNullOrEmpty(payload.Event.Id))
        {
            return RevenueCatWebhookReadResult.Malformed();
        }

        return RevenueCatWebhookReadResult.Success(payload, rawBody);
    }
}
