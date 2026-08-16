using System.Text;
using System.Text.Json;

namespace SyntaxCircus.RevenueCat;

public enum RevenueCatWebhookStatus
{
    Success,
    Unauthorized,
    Malformed,
}

public sealed record RevenueCatWebhookReadResult(
    RevenueCatWebhookStatus Status,
    RevenueCatWebhookPayload? Payload = null,
    string? RawBody = null)
{
    public static RevenueCatWebhookReadResult Unauthorized() => new(RevenueCatWebhookStatus.Unauthorized);

    public static RevenueCatWebhookReadResult Malformed() => new(RevenueCatWebhookStatus.Malformed);

    public static RevenueCatWebhookReadResult Success(RevenueCatWebhookPayload payload, string rawBody)
        => new(RevenueCatWebhookStatus.Success, payload, rawBody);
}

/// <summary>
/// Reads and verifies an inbound RevenueCat webhook request: buffers the raw body (so it can be
/// both HMAC-verified and JSON-deserialized), verifies the <c>X-RevenueCat-Signature</c> header
/// against <see cref="RevenueCatOptions.WebhookSecret"/>, then deserializes the envelope. Storing
/// the event for idempotency and dispatching it for processing is left to the caller — this only
/// answers "is this request genuinely from RevenueCat, and what does it say".
/// </summary>
public static class RevenueCatWebhookReader
{
    private const string SignatureHeaderName = "X-RevenueCat-Signature";

    public static async Task<RevenueCatWebhookReadResult> ReadAndVerifyAsync(
        HttpRequest request,
        RevenueCatOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);

        request.EnableBuffering();
        using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
        var rawBody = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        request.Body.Position = 0;

        if (!string.IsNullOrEmpty(options.WebhookSecret))
        {
            var signature = request.Headers[SignatureHeaderName].FirstOrDefault();
            if (string.IsNullOrEmpty(signature) || !RevenueCatSignatureVerifier.Verify(rawBody, signature, options.WebhookSecret))
            {
                return RevenueCatWebhookReadResult.Unauthorized();
            }
        }
        else if (options.RequireWebhookSecret)
        {
            return RevenueCatWebhookReadResult.Unauthorized();
        }

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
