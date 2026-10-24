namespace SyntaxCircus.RevenueCat.Tests;

public class RevenueCatSubscriberAliasClientTests
{
    private static (RevenueCatSubscriberAliasClient Client, StubHttpMessageHandler Handler) CreateClient(
        Func<HttpRequestMessage, HttpResponseMessage> responder,
        RevenueCatOptions? options = null)
    {
        var handler = new StubHttpMessageHandler(responder);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.revenuecat.test/") };
        var client = new RevenueCatSubscriberAliasClient(httpClient, Options.Create(options ?? new RevenueCatOptions { ApiKey = "secret_key" }));
        return (client, handler);
    }

    [Fact]
    public async Task CreateAliasAsync_Success_SendsExpectedRequest()
    {
        var (client, handler) = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK));

        await client.CreateAliasAsync("canonical-user", "anon-user", TestContext.Current.CancellationToken);

        handler.LastRequest!.Method.ShouldBe(HttpMethod.Post);
        handler.LastRequest.RequestUri!.ToString().ShouldContain("v1/subscribers/anon-user/alias");
        handler.LastRequest.HeaderValue("Authorization").ShouldBe("Bearer secret_key");
        handler.LastRequest.Body!.ShouldContain("canonical-user");
    }

    [Fact]
    public async Task CreateAliasAsync_ForbiddenWithV1IncompatibilityCode_ThrowsWithGuidanceMessage()
    {
        var (client, _) = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("{\"code\":7723,\"message\":\"not compatible\"}"),
        });

        var exception = await Should.ThrowAsync<InvalidOperationException>(() =>
            client.CreateAliasAsync("canonical-user", "anon-user", TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("RevenueCat:ApiKey");
    }

    [Fact]
    public async Task CreateAliasAsync_BothKeysConfigured_UsesTheSecretKey()
    {
        var (client, handler) = CreateClient(
            _ => new HttpResponseMessage(HttpStatusCode.OK),
            new RevenueCatOptions { ApiKey = "secret_key", PublicApiKey = "public_key" });

        await client.CreateAliasAsync("canonical-user", "anon-user", TestContext.Current.CancellationToken);

        handler.LastRequest!.HeaderValue("Authorization").ShouldBe("Bearer secret_key");
    }

    [Fact]
    public async Task CreateAliasAsync_OnlyPublicKeyConfigured_ThrowsWithoutCallingRevenueCat()
    {
        // RevenueCat rejects aliasing with a public key (HTTP 403, code 8185), so don't even try.
        var (client, handler) = CreateClient(
            _ => new HttpResponseMessage(HttpStatusCode.OK),
            new RevenueCatOptions { PublicApiKey = "public_key" });

        var exception = await Should.ThrowAsync<InvalidOperationException>(() =>
            client.CreateAliasAsync("canonical-user", "anon-user", TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("RevenueCat:ApiKey");
        handler.LastRequest.ShouldBeNull();
    }

    [Fact]
    public async Task CreateAliasAsync_OtherFailure_ThrowsWithStatusCodeMessage()
    {
        var (client, _) = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("server exploded"),
        });

        var exception = await Should.ThrowAsync<InvalidOperationException>(() =>
            client.CreateAliasAsync("canonical-user", "anon-user", TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("500");
    }

    [Fact]
    public async Task CreateAliasAsync_NullCanonicalAppUserId_ThrowsArgumentException()
    {
        var (client, _) = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK));

        await Should.ThrowAsync<ArgumentException>(() =>
            client.CreateAliasAsync(null!, "anon-user", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreateAliasAsync_EmptyAnonymousAppUserId_ThrowsArgumentException()
    {
        var (client, _) = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK));

        await Should.ThrowAsync<ArgumentException>(() =>
            client.CreateAliasAsync("canonical-user", string.Empty, TestContext.Current.CancellationToken));
    }
}
