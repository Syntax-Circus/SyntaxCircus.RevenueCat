using System.Security.Cryptography;

namespace SyntaxCircus.RevenueCat.Tests;

public class RevenueCatWebhookReaderTests
{
    private const string Secret = "whsec_test_secret";

    private static string ComputeHeader(string body, string secret, DateTimeOffset timestamp)
    {
        var unixTimestamp = timestamp.ToUnixTimeSeconds();
        var payload = Encoding.UTF8.GetBytes($"{unixTimestamp}.{body}");
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var computed = hmac.ComputeHash(payload);
        return $"t={unixTimestamp},v1={Convert.ToHexString(computed).ToLowerInvariant()}";
    }

    private static DefaultHttpContext CreateContext(string body, string? signature, long? contentLength = null)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(bodyBytes);
        context.Request.ContentLength = contentLength ?? bodyBytes.Length;
        if (signature is not null)
        {
            context.Request.Headers["X-RevenueCat-Webhook-Signature"] = signature;
        }

        return context;
    }

    [Fact]
    public async Task ReadAndVerifyAsync_ValidSignature_ReturnsSuccessWithParsedPayload()
    {
        const string body = "{\"api_version\":\"1.0\",\"event\":{\"id\":\"evt_1\",\"type\":\"INITIAL_PURCHASE\"}}";
        var context = CreateContext(body, ComputeHeader(body, Secret, DateTimeOffset.UtcNow));
        var options = new RevenueCatOptions { WebhookSecret = Secret };

        var result = await RevenueCatWebhookReader.ReadAndVerifyAsync(context.Request, options, TestContext.Current.CancellationToken);

        result.Status.ShouldBe(RevenueCatWebhookStatus.Success);
        result.Payload!.Event.Id.ShouldBe("evt_1");
        result.Payload.Event.Type.ShouldBe("INITIAL_PURCHASE");
        result.RawBody.ShouldBe(body);
    }

    [Fact]
    public async Task ReadAndVerifyAsync_TamperedBody_ReturnsUnauthorized()
    {
        const string signedBody = "{\"event\":{\"id\":\"evt_1\"}}";
        const string tamperedBody = "{\"event\":{\"id\":\"evt_evil\"}}";
        var context = CreateContext(tamperedBody, ComputeHeader(signedBody, Secret, DateTimeOffset.UtcNow));
        var options = new RevenueCatOptions { WebhookSecret = Secret };

        var result = await RevenueCatWebhookReader.ReadAndVerifyAsync(context.Request, options, TestContext.Current.CancellationToken);

        result.Status.ShouldBe(RevenueCatWebhookStatus.Unauthorized);
    }

    [Fact]
    public async Task ReadAndVerifyAsync_StaleTimestampOutsideTolerance_ReturnsUnauthorized()
    {
        const string body = "{\"event\":{\"id\":\"evt_1\"}}";
        var options = new RevenueCatOptions { WebhookSecret = Secret };
        var staleTimestamp = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(options.WebhookSignatureToleranceSeconds + 1);
        var context = CreateContext(body, ComputeHeader(body, Secret, staleTimestamp));

        var result = await RevenueCatWebhookReader.ReadAndVerifyAsync(context.Request, options, TestContext.Current.CancellationToken);

        result.Status.ShouldBe(RevenueCatWebhookStatus.Unauthorized);
    }

    [Fact]
    public async Task ReadAndVerifyAsync_MissingSignatureHeader_ReturnsUnauthorized()
    {
        const string body = "{\"event\":{\"id\":\"evt_1\"}}";
        var context = CreateContext(body, signature: null);
        var options = new RevenueCatOptions { WebhookSecret = Secret };

        var result = await RevenueCatWebhookReader.ReadAndVerifyAsync(context.Request, options, TestContext.Current.CancellationToken);

        result.Status.ShouldBe(RevenueCatWebhookStatus.Unauthorized);
    }

    [Fact]
    public async Task ReadAndVerifyAsync_NoSecretConfiguredAndRequired_ReturnsUnauthorized()
    {
        const string body = "{\"event\":{\"id\":\"evt_1\"}}";
        var context = CreateContext(body, signature: null);
        var options = new RevenueCatOptions { WebhookSecret = null, RequireWebhookSecret = true };

        var result = await RevenueCatWebhookReader.ReadAndVerifyAsync(context.Request, options, TestContext.Current.CancellationToken);

        result.Status.ShouldBe(RevenueCatWebhookStatus.Unauthorized);
    }

    [Fact]
    public async Task ReadAndVerifyAsync_NoSecretConfiguredButNotRequired_AcceptsUnverified()
    {
        const string body = "{\"event\":{\"id\":\"evt_1\"}}";
        var context = CreateContext(body, signature: null);
        var options = new RevenueCatOptions { WebhookSecret = null, RequireWebhookSecret = false };

        var result = await RevenueCatWebhookReader.ReadAndVerifyAsync(context.Request, options, TestContext.Current.CancellationToken);

        result.Status.ShouldBe(RevenueCatWebhookStatus.Success);
        result.Payload!.Event.Id.ShouldBe("evt_1");
    }

    [Fact]
    public async Task ReadAndVerifyAsync_MalformedJson_ReturnsMalformed()
    {
        const string body = "not json at all";
        var context = CreateContext(body, ComputeHeader(body, Secret, DateTimeOffset.UtcNow));
        var options = new RevenueCatOptions { WebhookSecret = Secret };

        var result = await RevenueCatWebhookReader.ReadAndVerifyAsync(context.Request, options, TestContext.Current.CancellationToken);

        result.Status.ShouldBe(RevenueCatWebhookStatus.Malformed);
    }

    [Fact]
    public async Task ReadAndVerifyAsync_MissingEventId_ReturnsMalformed()
    {
        const string body = "{\"event\":{\"type\":\"INITIAL_PURCHASE\"}}";
        var context = CreateContext(body, ComputeHeader(body, Secret, DateTimeOffset.UtcNow));
        var options = new RevenueCatOptions { WebhookSecret = Secret };

        var result = await RevenueCatWebhookReader.ReadAndVerifyAsync(context.Request, options, TestContext.Current.CancellationToken);

        result.Status.ShouldBe(RevenueCatWebhookStatus.Malformed);
    }

    [Fact]
    public async Task ReadAndVerifyAsync_ContentLengthOverMaxBodyBytes_ReturnsTooLarge()
    {
        const string body = "{\"event\":{\"id\":\"evt_1\"}}";
        var options = new RevenueCatOptions { WebhookSecret = Secret, WebhookMaxBodyBytes = 8 };
        var context = CreateContext(body, ComputeHeader(body, Secret, DateTimeOffset.UtcNow));

        var result = await RevenueCatWebhookReader.ReadAndVerifyAsync(context.Request, options, TestContext.Current.CancellationToken);

        result.Status.ShouldBe(RevenueCatWebhookStatus.TooLarge);
    }

    [Fact]
    public async Task ReadAndVerifyAsync_BodyUnderMaxBodyBytes_Unaffected()
    {
        const string body = "{\"event\":{\"id\":\"evt_1\"}}";
        var options = new RevenueCatOptions { WebhookSecret = Secret, WebhookMaxBodyBytes = 1024 };
        var context = CreateContext(body, ComputeHeader(body, Secret, DateTimeOffset.UtcNow));

        var result = await RevenueCatWebhookReader.ReadAndVerifyAsync(context.Request, options, TestContext.Current.CancellationToken);

        result.Status.ShouldBe(RevenueCatWebhookStatus.Success);
    }

    [Fact]
    public async Task ReadAndVerifyAsync_NullRequest_ThrowsArgumentNullException()
        => await Should.ThrowAsync<ArgumentNullException>(() =>
            RevenueCatWebhookReader.ReadAndVerifyAsync(null!, new RevenueCatOptions(), TestContext.Current.CancellationToken));

    [Fact]
    public async Task ReadAndVerifyAsync_NullOptions_ThrowsArgumentNullException()
    {
        var context = CreateContext("{}", signature: null);

        await Should.ThrowAsync<ArgumentNullException>(() =>
            RevenueCatWebhookReader.ReadAndVerifyAsync(context.Request, null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadAndVerifyAsync_ValidSignature_RewindsBodyPositionToStart()
    {
        const string body = "{\"event\":{\"id\":\"evt_1\"}}";
        var context = CreateContext(body, ComputeHeader(body, Secret, DateTimeOffset.UtcNow));
        var options = new RevenueCatOptions { WebhookSecret = Secret };

        await RevenueCatWebhookReader.ReadAndVerifyAsync(context.Request, options, TestContext.Current.CancellationToken);

        context.Request.Body.Position.ShouldBe(0);
    }
}
