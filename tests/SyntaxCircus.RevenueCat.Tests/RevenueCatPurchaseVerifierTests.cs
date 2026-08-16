namespace SyntaxCircus.RevenueCat.Tests;

public class RevenueCatPurchaseVerifierTests
{
    private const string SubscriberWithMatchingTransactionJson = """
        {
          "subscriber": {
            "subscriber_attributes": { "$email": { "value": "user@example.com" } },
            "non_subscriptions": {
              "product_1": [
                { "id": "txn_1", "purchase_date": "2026-01-15T00:00:00Z", "price": 9.99, "currency": "USD", "store": "app_store", "status": "completed" }
              ]
            }
          }
        }
        """;

    private const string SubscriberWithPendingTransactionJson = """
        {
          "subscriber": {
            "non_subscriptions": {
              "product_1": [
                { "id": "txn_1", "purchase_date": "2026-01-15T00:00:00Z", "price": 9.99, "currency": "USD", "status": "refunded" }
              ]
            }
          }
        }
        """;

    private const string SubscriberWithNoMatchJson = """
        {
          "subscriber": {
            "non_subscriptions": {}
          }
        }
        """;

    private static (RevenueCatPurchaseVerifier Verifier, StubHttpMessageHandler Handler, IRevenueCatTransactionService TransactionService) CreateVerifier(
        Func<HttpRequestMessage, HttpResponseMessage> responder,
        RevenueCatOptions? options = null)
    {
        var handler = new StubHttpMessageHandler(responder);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.revenuecat.test/") };
        var transactionService = Substitute.For<IRevenueCatTransactionService>();
        transactionService
            .GetTransactionsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<RevenueCatTransaction>());

        var verifier = new RevenueCatPurchaseVerifier(
            httpClient,
            Options.Create(options ?? new RevenueCatOptions { PublicApiKey = "public_key" }),
            transactionService,
            NullLogger<RevenueCatPurchaseVerifier>.Instance);

        return (verifier, handler, transactionService);
    }

    [Fact]
    public async Task VerifyAsync_NoCredentialsConfigured_ReturnsConfigurationError()
    {
        var (verifier, handler, _) = CreateVerifier(_ => new HttpResponseMessage(HttpStatusCode.OK), new RevenueCatOptions());

        var result = await verifier.VerifyAsync(new RevenueCatPurchaseVerificationRequest("user1", "product_1", null), TestContext.Current.CancellationToken);

        result.Status.ShouldBe("configuration_error");
        result.HttpStatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        handler.LastRequest.ShouldBeNull();
    }

    [Fact]
    public async Task VerifyAsync_SubscriberNotFoundInResponse_ReturnsNotFound()
    {
        var (verifier, _, _) = CreateVerifier(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"foo\":\"bar\"}", Encoding.UTF8, "application/json"),
        });

        var result = await verifier.VerifyAsync(new RevenueCatPurchaseVerificationRequest("user1", "product_1", null), TestContext.Current.CancellationToken);

        result.Status.ShouldBe("not_found");
        result.HttpStatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task VerifyAsync_SubscriberTransactionMatches_ReturnsVerified()
    {
        var (verifier, _, _) = CreateVerifier(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SubscriberWithMatchingTransactionJson, Encoding.UTF8, "application/json"),
        });

        var result = await verifier.VerifyAsync(new RevenueCatPurchaseVerificationRequest("user1", "product_1", null), TestContext.Current.CancellationToken);

        result.Status.ShouldBe("verified");
        result.Purchase!.TransactionId.ShouldBe("txn_1");
        result.Purchase.Price.ShouldBe(9.99m);
        result.Purchase.CheckoutEmail.ShouldBe("user@example.com");
        result.Purchase.AppUserId.ShouldBe("user1");
    }

    [Fact]
    public async Task VerifyAsync_SubscriberTransactionNotCompleted_ReturnsNotCompleted()
    {
        var (verifier, _, _) = CreateVerifier(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SubscriberWithPendingTransactionJson, Encoding.UTF8, "application/json"),
        });

        var result = await verifier.VerifyAsync(new RevenueCatPurchaseVerificationRequest("user1", "product_1", null), TestContext.Current.CancellationToken);

        result.Status.ShouldBe("not_completed");
        result.HttpStatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task VerifyAsync_NoSubscriberMatchButTransactionsFallbackMatches_UsesFallback()
    {
        var (verifier, _, transactionService) = CreateVerifier(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SubscriberWithNoMatchJson, Encoding.UTF8, "application/json"),
        });
        transactionService
            .GetTransactionsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([new RevenueCatTransaction
            {
                TransactionId = "txn_fallback",
                AppUserId = "user1",
                ProductId = "product_1",
                Price = 4.99m,
                Currency = "USD",
                Status = "completed",
                PurchasedAt = DateTimeOffset.UtcNow,
            }]);

        var result = await verifier.VerifyAsync(new RevenueCatPurchaseVerificationRequest("user1", "product_1", null), TestContext.Current.CancellationToken);

        result.Status.ShouldBe("verified");
        result.Purchase!.TransactionId.ShouldBe("txn_fallback");
        result.Purchase.Price.ShouldBe(4.99m);
    }

    [Fact]
    public async Task VerifyAsync_NoMatchAnywhere_ReturnsNotFound()
    {
        var (verifier, _, _) = CreateVerifier(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SubscriberWithNoMatchJson, Encoding.UTF8, "application/json"),
        });

        var result = await verifier.VerifyAsync(new RevenueCatPurchaseVerificationRequest("user1", "product_1", null), TestContext.Current.CancellationToken);

        result.Status.ShouldBe("not_found");
        result.Message.ShouldBe("RevenueCat has not finalized this purchase yet.");
    }

    [Fact]
    public async Task VerifyAsync_FirstCredentialForbidden_FallsBackToSecondCredential()
    {
        var options = new RevenueCatOptions { PublicApiKey = "public_key", ApiKey = "legacy_key" };
        var (verifier, _, _) = CreateVerifier(
            req =>
            {
                var isPrimary = req.Headers.Authorization!.Parameter == "public_key";
                return isPrimary
                    ? new HttpResponseMessage(HttpStatusCode.Forbidden)
                    : new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(SubscriberWithMatchingTransactionJson, Encoding.UTF8, "application/json"),
                    };
            },
            options);

        var result = await verifier.VerifyAsync(new RevenueCatPurchaseVerificationRequest("user1", "product_1", null), TestContext.Current.CancellationToken);

        result.Status.ShouldBe("verified");
    }

    [Fact]
    public async Task VerifyAsync_AllCredentialsFail_ReturnsRevenueCatUnavailable()
    {
        var (verifier, _, _) = CreateVerifier(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var result = await verifier.VerifyAsync(new RevenueCatPurchaseVerificationRequest("user1", "product_1", null), TestContext.Current.CancellationToken);

        result.Status.ShouldBe("revenuecat_unavailable");
        result.HttpStatusCode.ShouldBe(HttpStatusCode.BadGateway);
    }

    [Fact]
    public async Task VerifyAsync_HttpRequestException_ReturnsRevenueCatUnavailable()
    {
        var (verifier, _, _) = CreateVerifier(_ => throw new HttpRequestException("connection failed"));

        var result = await verifier.VerifyAsync(new RevenueCatPurchaseVerificationRequest("user1", "product_1", null), TestContext.Current.CancellationToken);

        result.Status.ShouldBe("revenuecat_unavailable");
    }

    [Fact]
    public async Task VerifyAsync_CancelledToken_PropagatesOperationCanceledException()
    {
        var (verifier, _, _) = CreateVerifier(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            verifier.VerifyAsync(new RevenueCatPurchaseVerificationRequest("user1", "product_1", null), cts.Token));
    }

    [Fact]
    public async Task VerifyAsync_NullRequest_ThrowsArgumentNullException()
    {
        var (verifier, _, _) = CreateVerifier(_ => new HttpResponseMessage(HttpStatusCode.OK));

        await Should.ThrowAsync<ArgumentNullException>(() => verifier.VerifyAsync(null!, TestContext.Current.CancellationToken));
    }
}
