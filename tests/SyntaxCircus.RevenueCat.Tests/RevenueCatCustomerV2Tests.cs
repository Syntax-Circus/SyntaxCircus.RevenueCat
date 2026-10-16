namespace SyntaxCircus.RevenueCat.Tests;

public class RevenueCatCustomerV2Tests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.FromUnixTimeMilliseconds(1700000000000);
    private static readonly DateTimeOffset End = Start.AddDays(1);

    private static RevenueCatOptions Configuration(string? project = "proj/a", string? key = "server-key", string? expected = null)
        => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CustomerApiVersion"] = "V2", ["ProjectId"] = project, ["ApiKey"] = key,
            ["PublicApiKey"] = "public-key", ["CustomerApiBaseUrl"] = "https://api.revenuecat.test/v2/",
            ["ExpectedTransactionEnvironment"] = expected,
        }).Build().Get<RevenueCatOptions>()!;

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };
    private static string Purchase(string status = "owned", string environment = "sandbox", string transaction = "\"store-tx\"", string gross = "4.99")
        => $$$"""{"id":"purch_123","product_id":"prod_123","customer_id":"user/a","store_purchase_identifier":{{{transaction}}},"status":"{{{status}}}","environment":"{{{environment}}}","store":"play_store","purchased_at":1700000000000,"revenue_in_usd":{"currency":"USD","gross":{{{gross}}}}}""";

    private static string Page(string items, string? next = null)
        => $$"""{"items":[{{items}}],"next_page":{{(next is null ? "null" : JsonSerializer.Serialize(next))}}}""";

    private static HttpClient Client(StubHttpMessageHandler handler) => new(handler) { BaseAddress = new Uri("https://api.revenuecat.test/") };
    private static RevenueCatTransactionService Transactions(HttpClient http, RevenueCatOptions? options = null)
        => new(http, Options.Create(options ?? Configuration()), NullLogger<RevenueCatTransactionService>.Instance);
    private static RevenueCatPurchaseVerifier Verifier(HttpClient http, RevenueCatOptions? options = null)
        => new(http, Options.Create(options ?? Configuration()), Substitute.For<IRevenueCatTransactionService>(), NullLogger<RevenueCatPurchaseVerifier>.Instance);

    private static HttpResponseMessage Respond(HttpRequestMessage request, string? purchases = null)
        => request.RequestUri!.AbsolutePath.Contains("/products/", StringComparison.Ordinal)
            ? Json("""{"id":"prod_123","store_identifier":"coins","app_id":"app_1"}""")
            : Json(Page(purchases ?? Purchase()));

    [Theory]
    [InlineData("owned")]
    public async Task Verify_CompletedPurchase_UsesV2StoreIdentityAndMoney(string status)
    {
        var handler = new StubHttpMessageHandler(request => Respond(request, Purchase(status)));
        using var http = Client(handler);
        var result = await Verifier(http).VerifyAsync(new("user/a", "coins", "store-tx"), TestContext.Current.CancellationToken);
        result.Status.ShouldBe("verified");
        result.Purchase!.TransactionId.ShouldBe("store-tx");
        result.Purchase.Price.ShouldBe(4.99m);
        result.Purchase.Currency.ShouldBe("USD");
        result.Purchase.IsSandbox.ShouldBe(true);
        result.Purchase.Store.ShouldBe("PLAY_STORE");
        result.Purchase.PurchasedAt.ShouldBe(Start);
        handler.Requests[0].RequestUri!.AbsoluteUri.ShouldBe("https://api.revenuecat.test/v2/projects/proj%2Fa/customers/user%2Fa/purchases?limit=100");
        handler.Requests.ShouldAllBe(request => request.HeaderValue("Authorization") == "Bearer server-key");
    }

    [Theory]
    [InlineData("refunded", "sandbox", "not_completed")]
    [InlineData("owned", "production", "environment_mismatch")]
    public async Task Verify_RefundOrEnvironmentMismatch_DoesNotGrantPurchase(string status, string environment, string expected)
    {
        var handler = new StubHttpMessageHandler(request => Respond(request, Purchase(status, environment)));
        using var http = Client(handler);
        var result = await Verifier(http, Configuration(expected: "Sandbox")).VerifyAsync(new("user/a", "coins", "store-tx"), TestContext.Current.CancellationToken);
        result.Status.ShouldBe(expected);
        result.Purchase.ShouldBeNull();
    }

    [Theory]
    [InlineData("unknown", "sandbox", "\"store-tx\"", "4.99")]
    [InlineData("owned", "unknown", "\"store-tx\"", "4.99")]
    [InlineData("owned", "sandbox", "null", "4.99")]
    [InlineData("owned", "sandbox", "\"store-tx\"", "{\"amount\":4.99}")]
    public async Task Verify_MalformedAuthoritativeFields_DoesNotGrantPurchase(string status, string environment, string transaction, string gross)
    {
        var handler = new StubHttpMessageHandler(request => Respond(request, Purchase(status, environment, transaction, gross)));
        using var http = Client(handler);
        var result = await Verifier(http).VerifyAsync(new("user/a", "coins", null), TestContext.Current.CancellationToken);
        result.Status.ShouldBe("revenuecat_unavailable");
        result.Purchase.ShouldBeNull();
    }

    [Fact]
    public async Task Reconcile_PurchasePagination_NumericTransactionAndInclusiveDates()
    {
        var page = 0;
        var handler = new StubHttpMessageHandler(request => request.RequestUri!.AbsolutePath.Contains("/products/", StringComparison.Ordinal)
            ? Respond(request)
            : Json(++page == 1 ? Page(Purchase(transaction: "123"), "/v2/projects/proj%2Fa/customers/user%2Fa/purchases?starting_after=p1") : Page(Purchase(transaction: "456"))));
        using var http = Client(handler);
        var result = await Transactions(http).GetTransactionsForCandidatesAsync(["user/a", "user/a"], Start, End, TestContext.Current.CancellationToken);
        result.Select(transaction => transaction.TransactionId).ShouldBe(["123", "456"]);
        result.ShouldAllBe(transaction => transaction.ProductId == "coins" && transaction.Price == 4.99m);
        result[0].Metadata["revenuecat_app_id"].ShouldBe("app_1");
        var outside = await Transactions(http).GetTransactionsForCandidatesAsync(["user/a"], End, End.AddDays(1), TestContext.Current.CancellationToken);
        outside.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("https://evil.test/v2/projects/proj%2Fa/customers/user%2Fa/purchases")]
    [InlineData("/v2/projects/other/customers/user%2Fa/purchases")]
    [InlineData("/v2/projects/proj%2Fa/customers/other/purchases")]
    [InlineData("/v2/projects/proj%2Fa/customers/user%2Fa/purchases/extra")]
    [InlineData("/v2/projects/proj%2Fa/customers/user%2Fa/purchases?limit=100")]
    public async Task Reconcile_UnsafeOrCyclicPage_ThrowsBeforeSendingNextRequest(string next)
    {
        var handler = new StubHttpMessageHandler(_ => Json(Page("", next)));
        using var http = Client(handler);
        await Should.ThrowAsync<HttpRequestException>(() => Transactions(http).GetTransactionsForCandidatesAsync(["user/a"], Start, End, TestContext.Current.CancellationToken));
        handler.Requests.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Reconcile_HttpFailure_PreservesStatusWithoutPayloadOrFallback(HttpStatusCode status)
    {
        var handler = new StubHttpMessageHandler(_ => new(status) { Content = new StringContent("secret-payload") });
        using var http = Client(handler);
        var error = await Should.ThrowAsync<HttpRequestException>(() => Transactions(http).GetTransactionsForCandidatesAsync(["user/a"], Start, End, TestContext.Current.CancellationToken));
        error.StatusCode.ShouldBe(status);
        error.Message.ShouldNotContain("secret-payload");
        handler.Requests.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData(null, "server-key")]
    [InlineData("project", null)]
    public async Task Reconcile_MissingV2Configuration_ThrowsWithoutV1Fallback(string? project, string? key)
    {
        var handler = new StubHttpMessageHandler(_ => Json(Page("")));
        using var http = Client(handler);
        await Should.ThrowAsync<InvalidOperationException>(() => Transactions(http, Configuration(project, key)).GetTransactionsForCandidatesAsync(["user/a"], Start, End, TestContext.Current.CancellationToken));
        handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Reconcile_MalformedCollection_Throws()
    {
        var handler = new StubHttpMessageHandler(_ => Json("{}"));
        using var http = Client(handler);
        await Should.ThrowAsync<HttpRequestException>(() => Transactions(http).GetTransactionsAsync(Start, End, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Reconcile_Cancelled_DoesNotSend()
    {
        var handler = new StubHttpMessageHandler(_ => Json(Page("")));
        using var http = Client(handler);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => Transactions(http).GetTransactionsForCandidatesAsync(["user/a"], Start, End, cancellation.Token));
        handler.Requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(HttpStatusCode.NoContent, RevenueCatSubscriberDeletionResult.Deleted)]
    [InlineData(HttpStatusCode.NotFound, RevenueCatSubscriberDeletionResult.NotFound)]
    public async Task Delete_UsesProjectCustomerAndServerKey(HttpStatusCode status, RevenueCatSubscriberDeletionResult expected)
    {
        var handler = new StubHttpMessageHandler(_ => new(status));
        using var http = Client(handler);
        var result = await new RevenueCatSubscriberDeletionClient(http, Options.Create(Configuration())).DeleteSubscriberAsync("user/a", TestContext.Current.CancellationToken);
        result.ShouldBe(expected);
        handler.LastRequest!.RequestUri!.AbsoluteUri.ShouldBe("https://api.revenuecat.test/v2/projects/proj%2Fa/customers/user%2Fa");
        handler.LastRequest.Method.ShouldBe(HttpMethod.Delete);
        handler.LastRequest.HeaderValue("Authorization").ShouldBe("Bearer server-key");
    }

    [Theory]
    [InlineData("\"purchased_at\":\"yesterday\"")]
    [InlineData("\"purchased_at\":null")]
    [InlineData("\"purchased_at\":999999999999999999")]
    public async Task Verify_InvalidTimestamp_ReturnsUnavailable(string timestamp)
    {
        var handler = new StubHttpMessageHandler(request => Respond(request, Purchase().Replace("\"purchased_at\":1700000000000", timestamp, StringComparison.Ordinal)));
        using var http = Client(handler);
        var result = await Verifier(http).VerifyAsync(new("user/a", "coins", null), TestContext.Current.CancellationToken);
        result.Status.ShouldBe("revenuecat_unavailable");
    }

    [Fact]
    public async Task Reconcile_ProjectScan_PagesCustomersAndKeepsDistinctAppStoreEnvironmentTransactions()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/customers", StringComparison.Ordinal))
            {
                return Json(string.IsNullOrEmpty(request.RequestUri.Query) || request.RequestUri.Query == "?limit=100"
                    ? Page("""{"id":"user/a"}""", "/v2/projects/proj%2Fa/customers?starting_after=user1")
                    : Page("""{"id":"user/b"}"""));
            }

            if (path.Contains("/products/", StringComparison.Ordinal))
            {
                return Json(path.EndsWith("prod_123", StringComparison.Ordinal)
                    ? """{"store_identifier":"coins","app_id":"app_1"}"""
                    : """{"store_identifier":"coins","app_id":"app_2"}""");
            }

            return Json(Page(path.Contains("user%2Fa", StringComparison.Ordinal)
                ? Purchase() + "," + Purchase(environment: "production")
                : Purchase().Replace("user/a", "user/b", StringComparison.Ordinal).Replace("prod_123", "prod_456", StringComparison.Ordinal)));
        });
        using var http = Client(handler);
        var result = await Transactions(http).GetTransactionsAsync(Start, End, TestContext.Current.CancellationToken);
        result.Count.ShouldBe(3);
        result.Select(transaction => transaction.AppUserId).Distinct().Count().ShouldBe(2);
        handler.Requests.Count(request => request.RequestUri!.AbsolutePath.Contains("/products/", StringComparison.Ordinal)).ShouldBe(2);
    }

    [Theory]
    [InlineData("app_1", "verified", 1)]
    [InlineData("other_app", "not_found", 0)]
    public async Task CustomerAppAllowlist_FiltersVerificationAndReconciliation(string app, string expectedStatus, int expectedCount)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CustomerApiVersion"] = "V2", ["ApiKey"] = "server-key", ["ProjectId"] = "project",
            ["CustomerApiBaseUrl"] = "https://api.revenuecat.test/v2/", ["CustomerAppIds:0"] = app,
        }).Build().Get<RevenueCatOptions>()!;
        var handler = new StubHttpMessageHandler(request => Respond(request));
        using var http = Client(handler);
        var result = await Verifier(http, configuration).VerifyAsync(new("user/a", "coins", "store-tx"), TestContext.Current.CancellationToken);
        result.Status.ShouldBe(expectedStatus);
        var transactions = await Transactions(http, configuration).GetTransactionsForCandidatesAsync(["user/a"], Start, End, TestContext.Current.CancellationToken);
        transactions.Count.ShouldBe(expectedCount);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{\"items\":[],\"next_page\":123}")]
    [InlineData("{\"items\":[],\"next_page\":\"\"}")]
    public async Task Reconcile_MalformedPage_ThrowsHttpRequestException(string payload)
    {
        var handler = new StubHttpMessageHandler(_ => Json(payload));
        using var http = Client(handler);
        await Should.ThrowAsync<HttpRequestException>(() => Transactions(http).GetTransactionsForCandidatesAsync(["user/a"], Start, End, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(HttpStatusCode.Redirect)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Delete_Failure_PreservesStatusAndHidesPayload(HttpStatusCode status)
    {
        var handler = new StubHttpMessageHandler(_ => new(status) { Content = new StringContent("secret-payload") });
        using var http = Client(handler);
        var error = await Should.ThrowAsync<HttpRequestException>(() => new RevenueCatSubscriberDeletionClient(http, Options.Create(Configuration())).DeleteSubscriberAsync("user/a", TestContext.Current.CancellationToken));
        error.StatusCode.ShouldBe(status);
        error.Message.ShouldNotContain("secret-payload");
    }

    [Fact]
    public async Task Reconcile_RedirectedResponse_RejectsResult()
    {
        var handler = new StubHttpMessageHandler(_ => new(HttpStatusCode.OK)
        {
            Content = new StringContent(Page("")),
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://different.test/"),
        });
        using var http = Client(handler);
        await Should.ThrowAsync<HttpRequestException>(() => Transactions(http).GetTransactionsForCandidatesAsync(["user/a"], Start, End, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Reconcile_PageLimit_RejectsUnboundedPagination()
    {
        var count = 0;
        var handler = new StubHttpMessageHandler(_ => Json(Page("", $"/v2/projects/proj%2Fa/customers/user%2Fa/purchases?cursor={++count}")));
        using var http = Client(handler);
        await Should.ThrowAsync<HttpRequestException>(() => Transactions(http).GetTransactionsForCandidatesAsync(["user/a"], Start, End, TestContext.Current.CancellationToken));
        handler.Requests.Count.ShouldBe(1000);
    }

    [Fact]
    public async Task Verify_V2HttpFailure_DoesNotTryV1CredentialsOrReconciliation()
    {
        var handler = new StubHttpMessageHandler(_ => new(HttpStatusCode.Unauthorized));
        using var http = Client(handler);
        var result = await Verifier(http).VerifyAsync(new("user/a", "coins", "store-tx"), TestContext.Current.CancellationToken);
        result.Status.ShouldBe("revenuecat_unavailable");
        handler.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public void Options_DefaultVersion_RemainsV1AndProductSyncIndependent()
    {
        new RevenueCatOptions().CustomerApiVersion.ShouldBe(RevenueCatCustomerApiVersion.V1);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRevenueCat(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RevenueCat:CustomerApiVersion"] = "V2", ["RevenueCat:ApiKey"] = "server-key",
            ["RevenueCat:ProjectId"] = "project", ["RevenueCat:CustomerApiBaseUrl"] = "https://customer.test/v2/",
            ["RevenueCat:ApiBaseUrl"] = "https://legacy.test/", ["RevenueCat:ProductSyncApiBaseUrl"] = "https://products.test/v2/",
        }).Build());
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IOptions<RevenueCatOptions>>().Value.CustomerApiVersion.ShouldBe(RevenueCatCustomerApiVersion.V2);
        provider.GetRequiredService<IRevenueCatPurchaseVerifier>().ShouldNotBeNull();
        provider.GetRequiredService<IRevenueCatTransactionService>().ShouldNotBeNull();
        provider.GetRequiredService<IRevenueCatSubscriberDeletionClient>().ShouldNotBeNull();
        using var alias = provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(IRevenueCatSubscriberAliasClient));
        using var product = provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(IRevenueCatProductCatalogService));
        alias.BaseAddress.ShouldBe(new Uri("https://legacy.test/"));
        product.BaseAddress.ShouldBe(new Uri("https://products.test/v2/"));
    }

    [Fact]
    public async Task Reconcile_OmittedNextPage_TreatsPurchaseAndCustomerCollectionsAsComplete()
    {
        var handler = new StubHttpMessageHandler(request => request.RequestUri!.AbsolutePath.EndsWith("/customers", StringComparison.Ordinal)
            ? Json("""{"items":[{"id":"user/a"}]}""")
            : request.RequestUri.AbsolutePath.Contains("/products/", StringComparison.Ordinal)
                ? Respond(request)
                : Json("{\"items\":[" + Purchase() + "]}"));
        using var http = Client(handler);
        var result = await Transactions(http).GetTransactionsAsync(Start, End, TestContext.Current.CancellationToken);
        result.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Verify_AliasScopedPurchase_PreservesCanonicalCustomerMetadata()
    {
        var handler = new StubHttpMessageHandler(request => Respond(request, Purchase().Replace("\"customer_id\":\"user/a\"", "\"customer_id\":\"canonical-user\"", StringComparison.Ordinal)));
        using var http = Client(handler);
        var result = await Verifier(http).VerifyAsync(new("user/a", "coins", "store-tx"), TestContext.Current.CancellationToken);
        result.Status.ShouldBe("verified");
        result.Purchase!.AppUserId.ShouldBe("user/a");
        result.Purchase.Metadata["revenuecat_customer_id"].ShouldBe("canonical-user");
    }
}
