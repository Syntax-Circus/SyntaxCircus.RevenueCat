using System.Globalization;

namespace SyntaxCircus.RevenueCat.Tests;

public class RevenueCatTransactionServiceTests
{
    private static (RevenueCatTransactionService Service, StubHttpMessageHandler Handler) CreateService(
        Func<HttpRequestMessage, HttpResponseMessage> responder,
        RevenueCatOptions? options = null)
    {
        var handler = new StubHttpMessageHandler(responder);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.revenuecat.test/") };
        var service = new RevenueCatTransactionService(
            httpClient,
            Options.Create(options ?? new RevenueCatOptions { PublicApiKey = "public_key" }),
            NullLogger<RevenueCatTransactionService>.Instance);
        return (service, handler);
    }

    private static HttpResponseMessage JsonResponse(object payload)
        => new(HttpStatusCode.OK) { Content = JsonContent.Create(payload) };

    [Fact]
    public async Task GetTransactionsAsync_NoApiKeyConfigured_ReturnsEmptyWithoutCallingApi()
    {
        var (service, handler) = CreateService(_ => JsonResponse(new { transactions = Array.Empty<object>() }), new RevenueCatOptions());

        var result = await service.GetTransactionsAsync(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        result.ShouldBeEmpty();
        handler.LastRequest.ShouldBeNull();
    }

    [Fact]
    public async Task GetTransactionsAsync_NoTransactionsEndpointConfigured_ReturnsEmptyWithoutCallingApi()
    {
        var options = new RevenueCatOptions { PublicApiKey = "public_key", TransactionsEndpoint = "  " };
        var (service, handler) = CreateService(_ => JsonResponse(new { transactions = Array.Empty<object>() }), options);

        var result = await service.GetTransactionsAsync(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        result.ShouldBeEmpty();
        handler.LastRequest.ShouldBeNull();
    }

    [Fact]
    public async Task GetTransactionsAsync_UsesFirstCredentialAsBearerToken()
    {
        var options = new RevenueCatOptions { PublicApiKey = "public_key", ApiKey = "legacy_key" };
        var (service, handler) = CreateService(_ => JsonResponse(new { transactions = Array.Empty<object>() }), options);

        await service.GetTransactionsAsync(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        handler.LastRequest!.HeaderValue("Authorization").ShouldBe("Bearer public_key");
    }

    [Fact]
    public async Task GetTransactionsAsync_TransactionsArrayShape_ParsesTransactions()
    {
        var start = DateTimeOffset.Parse("2026-01-01T00:00:00Z", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        var end = DateTimeOffset.Parse("2026-01-31T00:00:00Z", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        var (service, _) = CreateService(_ => JsonResponse(new
        {
            transactions = new[]
            {
                new
                {
                    transaction_id = "txn_1",
                    app_user_id = "user_1",
                    product_id = "product_1",
                    price = 9.99m,
                    currency = "USD",
                    store = "app_store",
                    status = "completed",
                    purchase_date = "2026-01-15T00:00:00Z",
                },
            },
        }));

        var result = await service.GetTransactionsAsync(start, end, TestContext.Current.CancellationToken);

        result.Count.ShouldBe(1);
        result[0].TransactionId.ShouldBe("txn_1");
        result[0].AppUserId.ShouldBe("user_1");
        result[0].ProductId.ShouldBe("product_1");
        result[0].Store.ShouldBe("APP_STORE");
    }

    [Fact]
    public async Task GetTransactionsAsync_ItemsArrayShape_ParsesTransactions()
    {
        var start = DateTimeOffset.Parse("2026-01-01T00:00:00Z", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        var end = DateTimeOffset.Parse("2026-01-31T00:00:00Z", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        var (service, _) = CreateService(_ => JsonResponse(new
        {
            items = new[]
            {
                new
                {
                    id = "txn_2",
                    app_user_id = "user_2",
                    store_product_id = "product_2",
                    purchase_date_ms = new DateTimeOffset(2026, 1, 10, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(),
                },
            },
        }));

        var result = await service.GetTransactionsAsync(start, end, TestContext.Current.CancellationToken);

        result.Count.ShouldBe(1);
        result[0].TransactionId.ShouldBe("txn_2");
        result[0].ProductId.ShouldBe("product_2");
    }

    [Fact]
    public async Task GetTransactionsAsync_SubscriberNonSubscriptionsShape_ParsesTransactions()
    {
        var start = DateTimeOffset.Parse("2026-01-01T00:00:00Z", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        var end = DateTimeOffset.Parse("2026-01-31T00:00:00Z", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        var (service, _) = CreateService(_ => JsonResponse(new
        {
            subscriber = new
            {
                non_subscriptions = new Dictionary<string, object>
                {
                    ["product_3"] = new[]
                    {
                        new { id = "txn_3", app_user_id = "user_3", product_id = "product_3", purchase_date = "2026-01-20T00:00:00Z" },
                    },
                },
            },
        }));

        var result = await service.GetTransactionsAsync(start, end, TestContext.Current.CancellationToken);

        result.Count.ShouldBe(1);
        result[0].TransactionId.ShouldBe("txn_3");
    }

    [Fact]
    public async Task GetTransactionsAsync_TransactionOutsideDateWindow_Excluded()
    {
        var start = DateTimeOffset.Parse("2026-01-01T00:00:00Z", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        var end = DateTimeOffset.Parse("2026-01-31T00:00:00Z", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        var (service, _) = CreateService(_ => JsonResponse(new
        {
            transactions = new[]
            {
                new { transaction_id = "txn_old", app_user_id = "user_1", product_id = "product_1", purchase_date = "2025-06-01T00:00:00Z" },
            },
        }));

        var result = await service.GetTransactionsAsync(start, end, TestContext.Current.CancellationToken);

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetTransactionsAsync_DuplicateTransactionIds_Deduplicated()
    {
        var start = DateTimeOffset.Parse("2026-01-01T00:00:00Z", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        var end = DateTimeOffset.Parse("2026-01-31T00:00:00Z", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        var (service, _) = CreateService(_ => JsonResponse(new
        {
            transactions = new[]
            {
                new { transaction_id = "txn_dup", app_user_id = "user_1", product_id = "product_1", purchase_date = "2026-01-05T00:00:00Z" },
            },
            items = new[]
            {
                new { id = "txn_dup", app_user_id = "user_1", product_id = "product_1", purchase_date = "2026-01-05T00:00:00Z" },
            },
        }));

        var result = await service.GetTransactionsAsync(start, end, TestContext.Current.CancellationToken);

        result.Count.ShouldBe(1);
    }

    [Fact]
    public async Task GetTransactionsAsync_NonSuccessStatusCode_ReturnsEmpty()
    {
        var (service, _) = CreateService(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var result = await service.GetTransactionsAsync(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetTransactionsAsync_HttpRequestException_ReturnsEmptyInsteadOfThrowing()
    {
        var (service, _) = CreateService(_ => throw new HttpRequestException("connection failed"));

        var result = await service.GetTransactionsAsync(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetTransactionsAsync_CancelledToken_PropagatesOperationCanceledException()
    {
        var (service, _) = CreateService(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            service.GetTransactionsAsync(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow, cts.Token));
    }
}
