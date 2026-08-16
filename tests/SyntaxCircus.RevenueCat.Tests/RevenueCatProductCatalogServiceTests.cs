namespace SyntaxCircus.RevenueCat.Tests;

public class RevenueCatProductCatalogServiceTests
{
    private static RevenueCatOptions DefaultOptions() => new()
    {
        ProductSyncApiKey = "sync_key",
        ProjectId = "proj1",
        ProductSyncAppId = "app1",
    };

    private static RevenueCatProductCatalogService CreateService(
        Func<HttpRequestMessage, HttpResponseMessage> responder,
        RevenueCatOptions? options = null)
    {
        var handler = new StubHttpMessageHandler(responder);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.revenuecat.test/v2/") };
        return new RevenueCatProductCatalogService(httpClient, Options.Create(options ?? DefaultOptions()), NullLogger<RevenueCatProductCatalogService>.Instance);
    }

    [Fact]
    public async Task PublishOneTimeProductAsync_NoExistingProduct_CreatesNewProduct()
    {
        var service = CreateService(req => req.Method == HttpMethod.Get
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"items\":[]}") }
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"id\":\"prod_new\"}") });

        var result = await service.PublishOneTimeProductAsync(new RevenueCatProductPublishRequest("sku1", "Widget"), TestContext.Current.CancellationToken);

        result.PublishedApps.Count.ShouldBe(1);
        result.PublishedApps[0].Created.ShouldBeTrue();
        result.PublishedApps[0].ProductId.ShouldBe("prod_new");
        result.PublishedApps[0].AppId.ShouldBe("app1");
    }

    [Fact]
    public async Task PublishOneTimeProductAsync_ExistingProductMatchesStoreIdentifier_Updates()
    {
        var service = CreateService(req => req.Method == HttpMethod.Get
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"items\":[{\"id\":\"prod_existing\",\"store_identifier\":\"sku1\"}]}") }
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"id\":\"prod_existing\"}") });

        var result = await service.PublishOneTimeProductAsync(new RevenueCatProductPublishRequest("sku1", "Widget"), TestContext.Current.CancellationToken);

        result.PublishedApps[0].Created.ShouldBeFalse();
        result.PublishedApps[0].ProductId.ShouldBe("prod_existing");
    }

    [Fact]
    public async Task PublishOneTimeProductAsync_PaginatedListing_FollowsNextPageToFindMatch()
    {
        var service = CreateService(req =>
        {
            if (req.Method != HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"id\":\"prod_page2\"}") };
            }

            return req.RequestUri!.ToString().Contains("page2", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"items\":[{\"id\":\"prod_page2\",\"store_identifier\":\"sku1\"}]}") }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"items\":[],\"next_page\":\"projects/proj1/products?page2\"}") };
        });

        var result = await service.PublishOneTimeProductAsync(new RevenueCatProductPublishRequest("sku1", "Widget"), TestContext.Current.CancellationToken);

        result.PublishedApps[0].ProductId.ShouldBe("prod_page2");
        result.PublishedApps[0].Created.ShouldBeFalse();
    }

    [Fact]
    public async Task PublishOneTimeProductAsync_MultipleAppIds_PublishesToEach()
    {
        var multiAppOptions = new RevenueCatOptions
        {
            ProductSyncApiKey = "sync_key",
            ProjectId = "proj1",
            ProductSyncAppIds = ["app1", "app2"],
        };
        var service = CreateService(
            req => req.Method == HttpMethod.Get
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"items\":[]}") }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"id\":\"prod_new\"}") },
            multiAppOptions);

        var result = await service.PublishOneTimeProductAsync(new RevenueCatProductPublishRequest("sku1", "Widget"), TestContext.Current.CancellationToken);

        result.PublishedApps.Count.ShouldBe(2);
        result.PublishedApps.Select(app => app.AppId).ShouldBe(["app1", "app2"]);
    }

    [Fact]
    public async Task PublishOneTimeProductAsync_MissingApiKey_ThrowsInvalidOperationException()
    {
        var options = new RevenueCatOptions { ProjectId = "proj1", ProductSyncAppId = "app1" };
        var service = CreateService(_ => new HttpResponseMessage(HttpStatusCode.OK), options);

        var exception = await Should.ThrowAsync<InvalidOperationException>(() =>
            service.PublishOneTimeProductAsync(new RevenueCatProductPublishRequest("sku1", "Widget"), TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("ProductSyncApiKey");
    }

    [Fact]
    public async Task PublishOneTimeProductAsync_MissingProjectId_ThrowsInvalidOperationException()
    {
        var options = new RevenueCatOptions { ProductSyncApiKey = "sync_key", ProductSyncAppId = "app1" };
        var service = CreateService(_ => new HttpResponseMessage(HttpStatusCode.OK), options);

        var exception = await Should.ThrowAsync<InvalidOperationException>(() =>
            service.PublishOneTimeProductAsync(new RevenueCatProductPublishRequest("sku1", "Widget"), TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("ProjectId");
    }

    [Fact]
    public async Task PublishOneTimeProductAsync_NoAppIdsConfigured_ThrowsInvalidOperationException()
    {
        var options = new RevenueCatOptions { ProductSyncApiKey = "sync_key", ProjectId = "proj1" };
        var service = CreateService(_ => new HttpResponseMessage(HttpStatusCode.OK), options);

        var exception = await Should.ThrowAsync<InvalidOperationException>(() =>
            service.PublishOneTimeProductAsync(new RevenueCatProductPublishRequest("sku1", "Widget"), TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("ProductSyncAppId");
    }

    [Fact]
    public async Task PublishOneTimeProductAsync_ListRequestFails_ThrowsWrappedRevenueCatProductSyncException()
    {
        var service = CreateService(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("{\"message\":\"boom\"}"),
        });

        var exception = await Should.ThrowAsync<RevenueCatProductSyncException>(() =>
            service.PublishOneTimeProductAsync(new RevenueCatProductPublishRequest("sku1", "Widget"), TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("app 'app1'");
        exception.Message.ShouldContain("boom");
    }

    [Fact]
    public async Task PublishOneTimeProductAsync_CreateResponseMissingId_ThrowsRevenueCatProductSyncException()
    {
        var service = CreateService(req => req.Method == HttpMethod.Get
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"items\":[]}") }
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"no_id_here\":true}") });

        await Should.ThrowAsync<RevenueCatProductSyncException>(() =>
            service.PublishOneTimeProductAsync(new RevenueCatProductPublishRequest("sku1", "Widget"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PublishOneTimeProductAsync_NullRequest_ThrowsArgumentNullException()
    {
        var service = CreateService(_ => new HttpResponseMessage(HttpStatusCode.OK));

        await Should.ThrowAsync<ArgumentNullException>(() =>
            service.PublishOneTimeProductAsync(null!, TestContext.Current.CancellationToken));
    }
}
