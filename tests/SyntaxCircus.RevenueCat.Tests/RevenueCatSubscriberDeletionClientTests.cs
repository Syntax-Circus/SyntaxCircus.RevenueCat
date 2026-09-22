namespace SyntaxCircus.RevenueCat.Tests;

public class RevenueCatSubscriberDeletionClientTests
{
    private static (RevenueCatSubscriberDeletionClient Client, StubHttpMessageHandler Handler) CreateClient(
        Func<HttpRequestMessage, HttpResponseMessage> responder,
        RevenueCatOptions? options = null)
    {
        var handler = new StubHttpMessageHandler(responder);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.revenuecat.test/") };
        var client = new RevenueCatSubscriberDeletionClient(httpClient, Options.Create(options ?? new RevenueCatOptions { ApiKey = "sk_secret_key" }));
        return (client, handler);
    }

    [Fact]
    public async Task DeleteSubscriberAsync_Success_SendsExpectedRequestAndReturnsDeleted()
    {
        var (client, handler) = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK));

        var result = await client.DeleteSubscriberAsync("app-user-1", TestContext.Current.CancellationToken);

        result.ShouldBe(RevenueCatSubscriberDeletionResult.Deleted);
        handler.LastRequest!.Method.ShouldBe(HttpMethod.Delete);
        handler.LastRequest.RequestUri!.ToString().ShouldContain("v1/subscribers/app-user-1");
        handler.LastRequest.HeaderValue("Authorization").ShouldBe("Bearer sk_secret_key");
    }

    [Fact]
    public async Task DeleteSubscriberAsync_UrlEscapesAppUserId()
    {
        var (client, handler) = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK));

        await client.DeleteSubscriberAsync("user with spaces/slash", TestContext.Current.CancellationToken);

        handler.LastRequest!.RequestUri!.AbsoluteUri.ShouldContain(Uri.EscapeDataString("user with spaces/slash"));
    }

    [Fact]
    public async Task DeleteSubscriberAsync_NotFound_ReturnsNotFoundWithoutThrowing()
    {
        var (client, _) = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await client.DeleteSubscriberAsync("app-user-1", TestContext.Current.CancellationToken);

        result.ShouldBe(RevenueCatSubscriberDeletionResult.NotFound);
    }

    [Fact]
    public async Task DeleteSubscriberAsync_ServerError_ThrowsHttpRequestExceptionWithStatusCode()
    {
        var (client, _) = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("server exploded"),
        });

        var exception = await Should.ThrowAsync<HttpRequestException>(() =>
            client.DeleteSubscriberAsync("app-user-1", TestContext.Current.CancellationToken));

        exception.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        exception.Message.ShouldContain("500");
    }

    [Fact]
    public async Task DeleteSubscriberAsync_TooManyRequests_ThrowsHttpRequestExceptionWithStatusCode()
    {
        var (client, _) = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));

        var exception = await Should.ThrowAsync<HttpRequestException>(() =>
            client.DeleteSubscriberAsync("app-user-1", TestContext.Current.CancellationToken));

        exception.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task DeleteSubscriberAsync_OnlyPublicApiKeyConfigured_ThrowsInvalidOperationException()
    {
        var (client, _) = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK), new RevenueCatOptions { PublicApiKey = "public_key" });

        var exception = await Should.ThrowAsync<InvalidOperationException>(() =>
            client.DeleteSubscriberAsync("app-user-1", TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("RevenueCat:ApiKey");
    }

    [Fact]
    public async Task DeleteSubscriberAsync_NoKeysConfigured_ThrowsInvalidOperationException()
    {
        var (client, _) = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK), new RevenueCatOptions());

        await Should.ThrowAsync<InvalidOperationException>(() =>
            client.DeleteSubscriberAsync("app-user-1", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteSubscriberAsync_NullAppUserId_ThrowsArgumentException()
    {
        var (client, _) = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK));

        await Should.ThrowAsync<ArgumentException>(() =>
            client.DeleteSubscriberAsync(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteSubscriberAsync_EmptyAppUserId_ThrowsArgumentException()
    {
        var (client, _) = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK));

        await Should.ThrowAsync<ArgumentException>(() =>
            client.DeleteSubscriberAsync(string.Empty, TestContext.Current.CancellationToken));
    }
}
