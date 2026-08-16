using System.Security.Cryptography;

namespace SyntaxCircus.RevenueCat.Tests;

public class RevenueCatWebhookReaderTests
{
    private const string Secret = "whsec_test_secret";

    private static string ComputeSignature(string body, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var computed = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
        return Convert.ToHexString(computed).ToLowerInvariant();
    }

    private static DefaultHttpContext CreateContext(string body, string? signature)
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        if (signature is not null)
        {
            context.Request.Headers["X-RevenueCat-Signature"] = signature;
        }

        return context;
    }

    [Fact]
    public async Task ReadAndVerifyAsync_ValidSignature_ReturnsSuccessWithParsedPayload()
    {
        const string body = "{\"api_version\":\"1.0\",\"event\":{\"id\":\"evt_1\",\"type\":\"INITIAL_PURCHASE\"}}";
        var context = CreateContext(body, ComputeSignature(body, Secret));
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
        var context = CreateContext(tamperedBody, ComputeSignature(signedBody, Secret));
        var options = new RevenueCatOptions { WebhookSecret = Secret };

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
        var context = CreateContext(body, ComputeSignature(body, Secret));
        var options = new RevenueCatOptions { WebhookSecret = Secret };

        var result = await RevenueCatWebhookReader.ReadAndVerifyAsync(context.Request, options, TestContext.Current.CancellationToken);

        result.Status.ShouldBe(RevenueCatWebhookStatus.Malformed);
    }

    [Fact]
    public async Task ReadAndVerifyAsync_MissingEventId_ReturnsMalformed()
    {
        const string body = "{\"event\":{\"type\":\"INITIAL_PURCHASE\"}}";
        var context = CreateContext(body, ComputeSignature(body, Secret));
        var options = new RevenueCatOptions { WebhookSecret = Secret };

        var result = await RevenueCatWebhookReader.ReadAndVerifyAsync(context.Request, options, TestContext.Current.CancellationToken);

        result.Status.ShouldBe(RevenueCatWebhookStatus.Malformed);
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
        var context = CreateContext(body, ComputeSignature(body, Secret));
        var options = new RevenueCatOptions { WebhookSecret = Secret };

        await RevenueCatWebhookReader.ReadAndVerifyAsync(context.Request, options, TestContext.Current.CancellationToken);

        context.Request.Body.Position.ShouldBe(0);
    }
}
