using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace SyntaxCircus.RevenueCat;

public sealed record RevenueCatPurchaseVerificationRequest(string AppUserId, string ProductId, string? TransactionId);

public sealed record RevenueCatVerifiedPurchase
{
    public string AppUserId { get; init; } = string.Empty;
    public string ProductId { get; init; } = string.Empty;
    public string TransactionId { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public string Currency { get; init; } = "USD";
    public string? Store { get; init; }
    public string Status { get; init; } = "completed";
    public DateTimeOffset PurchasedAt { get; init; }
    public string? CheckoutEmail { get; init; }
    public Dictionary<string, string?> Metadata { get; init; } = [];
}

public sealed record RevenueCatPurchaseVerificationResult
{
    public string Status { get; init; } = string.Empty;
    public string? Message { get; init; }
    public RevenueCatVerifiedPurchase? Purchase { get; init; }
    public HttpStatusCode HttpStatusCode { get; init; } = HttpStatusCode.OK;
}

public interface IRevenueCatPurchaseVerifier
{
    Task<RevenueCatPurchaseVerificationResult> VerifyAsync(
        RevenueCatPurchaseVerificationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed partial class RevenueCatPurchaseVerifier(
    HttpClient httpClient,
    IOptions<RevenueCatOptions> revenueCatOptions,
    IRevenueCatTransactionService revenueCatTransactions,
    ILogger<RevenueCatPurchaseVerifier> logger) : IRevenueCatPurchaseVerifier
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "RevenueCat purchase verification skipped: no verification API key is configured")]
    private static partial void LogMissingApiKey(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "RevenueCat purchase verification failed with HTTP {StatusCode} using {CredentialSource}. Body: {ResponseBody}")]
    private static partial void LogRequestFailed(ILogger logger, int statusCode, string credentialSource, string responseBody);

    [LoggerMessage(Level = LogLevel.Error, Message = "RevenueCat purchase verification request failed")]
    private static partial void LogRequestException(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "RevenueCat purchase verification used transactions fallback for {AppUserId}/{ProductId}")]
    private static partial void LogTransactionsFallbackUsed(ILogger logger, string appUserId, string productId);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "RevenueCat purchase verification subscriber match: tx={TransactionId}, product={ProductId}, price={Price} {Currency}, store={Store}, status={Status}, purchasedAt={PurchasedAt}")]
    private static partial void LogSubscriberMatch(
        ILogger logger, string transactionId, string productId, decimal price, string currency, string store, string status, DateTimeOffset purchasedAt);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "RevenueCat purchase verification transactions fallback match: tx={TransactionId}, product={ProductId}, price={Price} {Currency}, store={Store}, status={Status}, purchasedAt={PurchasedAt}")]
    private static partial void LogTransactionsFallbackMatch(
        ILogger logger, string transactionId, string productId, decimal price, string currency, string store, string status, DateTimeOffset purchasedAt);

    public async Task<RevenueCatPurchaseVerificationResult> VerifyAsync(
        RevenueCatPurchaseVerificationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var credentialCandidates = RevenueCatApiKeyResolver.ResolveV1CompatibleCredentials(revenueCatOptions.Value);
        if (credentialCandidates.Count == 0)
        {
            LogMissingApiKey(logger);
            return Failure("configuration_error", "A RevenueCat verification API key is required to verify purchases.", HttpStatusCode.ServiceUnavailable);
        }

        try
        {
            foreach (var (credentialSource, apiKey) in credentialCandidates)
            {
                using var httpRequest = new HttpRequestMessage(HttpMethod.Get, $"v1/subscribers/{Uri.EscapeDataString(request.AppUserId.Trim())}");
                httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                httpRequest.Headers.TryAddWithoutValidation("X-Platform", "web");
                httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                using var response = await httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    var responseBody = await ReadResponseBodyAsync(response, cancellationToken).ConfigureAwait(false);
                    LogRequestFailed(logger, (int)response.StatusCode, credentialSource, responseBody);
                    continue;
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                return await VerifyFromSubscriberAsync(document.RootElement, request, cancellationToken).ConfigureAwait(false);
            }

            return Failure("revenuecat_unavailable", "RevenueCat could not verify this purchase yet.", HttpStatusCode.BadGateway);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            LogRequestException(logger, ex);
            return Failure("revenuecat_unavailable", "RevenueCat could not verify this purchase yet.", HttpStatusCode.BadGateway);
        }
    }

    private static async Task<string> ReadResponseBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(body))
        {
            return "<empty>";
        }

        body = body.Trim();
        return body.Length <= 400 ? body : body[..400];
    }

    private async Task<RevenueCatPurchaseVerificationResult> VerifyFromSubscriberAsync(
        JsonElement root,
        RevenueCatPurchaseVerificationRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetSubscriber(root, out var subscriber))
        {
            return Failure("not_found", "RevenueCat subscriber was not found.", HttpStatusCode.NotFound);
        }

        var checkoutEmail = GetSubscriberAttribute(subscriber, "$email") ?? GetSubscriberAttribute(subscriber, "email");
        var transaction = FindTransaction(subscriber, request);
        var fallbackTransaction = await FindTransactionFromTransactionsApiAsync(request, cancellationToken).ConfigureAwait(false);

        if (transaction is not null)
        {
            LogSubscriberMatch(logger, transaction.TransactionId, transaction.ProductId, transaction.Price, transaction.Currency, transaction.Store ?? string.Empty, transaction.Status, transaction.PurchasedAt);
        }

        if (fallbackTransaction is not null)
        {
            LogTransactionsFallbackMatch(logger, fallbackTransaction.TransactionId, fallbackTransaction.ProductId, fallbackTransaction.Price, fallbackTransaction.Currency, fallbackTransaction.Store ?? string.Empty, fallbackTransaction.Status, fallbackTransaction.PurchasedAt);
        }

        if (transaction is null && fallbackTransaction is null)
        {
            return Failure("not_found", "RevenueCat has not finalized this purchase yet.", HttpStatusCode.NotFound);
        }

        var verifiedPurchase = transaction is null ? fallbackTransaction! : MergeTransaction(transaction, fallbackTransaction);

        if (fallbackTransaction is not null && (transaction is null || transaction.Price <= 0m))
        {
            var trimmedAppUserId = request.AppUserId.Trim();
            var trimmedProductId = request.ProductId.Trim();
            LogTransactionsFallbackUsed(logger, trimmedAppUserId, trimmedProductId);
        }

        if (!IsCompleted(verifiedPurchase))
        {
            return Failure("not_completed", "RevenueCat purchase exists but is not completed yet.", HttpStatusCode.Accepted);
        }

        return new RevenueCatPurchaseVerificationResult
        {
            Status = "verified",
            Purchase = verifiedPurchase with { AppUserId = request.AppUserId.Trim(), CheckoutEmail = checkoutEmail },
        };
    }

    private static bool TryGetSubscriber(JsonElement root, out JsonElement subscriber)
    {
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("subscriber", out subscriber) &&
            subscriber.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        subscriber = default;
        return false;
    }

    private static RevenueCatVerifiedPurchase? FindTransaction(JsonElement subscriber, RevenueCatPurchaseVerificationRequest request)
    {
        if (!subscriber.TryGetProperty("non_subscriptions", out var nonSubscriptions) || nonSubscriptions.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var productId = request.ProductId.Trim();
        var transactionId = request.TransactionId?.Trim();
        var candidates = new List<RevenueCatVerifiedPurchase>();
        foreach (var product in nonSubscriptions.EnumerateObject())
        {
            if (product.Value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var item in product.Value.EnumerateArray())
            {
                var transactionIds = GetTransactionIds(item);
                var mapped = MapTransaction(item, product.Name, transactionIds);
                if (mapped is null)
                {
                    continue;
                }

                if (!string.Equals(mapped.ProductId, productId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(transactionId) && !transactionIds.Contains(transactionId, StringComparer.Ordinal))
                {
                    continue;
                }

                candidates.Add(mapped);
            }
        }

        return candidates.OrderByDescending(candidate => candidate.PurchasedAt).FirstOrDefault();
    }

    private async Task<RevenueCatVerifiedPurchase?> FindTransactionFromTransactionsApiAsync(
        RevenueCatPurchaseVerificationRequest request,
        CancellationToken cancellationToken)
    {
        var startDate = DateTimeOffset.UtcNow.AddDays(-31);
        var endDate = DateTimeOffset.UtcNow.AddDays(1);
        var transactions = await revenueCatTransactions.GetTransactionsAsync(startDate, endDate, cancellationToken).ConfigureAwait(false);

        return transactions
            .Where(transaction =>
                string.Equals(transaction.AppUserId, request.AppUserId.Trim(), StringComparison.Ordinal) &&
                string.Equals(transaction.ProductId, request.ProductId.Trim(), StringComparison.Ordinal) &&
                (string.IsNullOrWhiteSpace(request.TransactionId) ||
                 string.Equals(transaction.TransactionId, request.TransactionId.Trim(), StringComparison.Ordinal)))
            .OrderByDescending(transaction => transaction.PurchasedAt)
            .Select(transaction => new RevenueCatVerifiedPurchase
            {
                AppUserId = transaction.AppUserId,
                ProductId = transaction.ProductId,
                TransactionId = transaction.TransactionId,
                Price = transaction.Price,
                Currency = transaction.Currency,
                Store = transaction.Store,
                Status = transaction.Status,
                PurchasedAt = transaction.PurchasedAt,
            })
            .FirstOrDefault();
    }

    private static RevenueCatVerifiedPurchase MergeTransaction(RevenueCatVerifiedPurchase subscriberTransaction, RevenueCatVerifiedPurchase? fallbackTransaction)
    {
        if (fallbackTransaction is null)
        {
            return subscriberTransaction;
        }

        return subscriberTransaction with
        {
            Price = subscriberTransaction.Price > 0m ? subscriberTransaction.Price : fallbackTransaction.Price,
            Currency = string.IsNullOrWhiteSpace(subscriberTransaction.Currency) || subscriberTransaction.Currency == "USD"
                ? fallbackTransaction.Currency
                : subscriberTransaction.Currency,
            Store = subscriberTransaction.Store ?? fallbackTransaction.Store,
            Status = subscriberTransaction.Status,
            PurchasedAt = subscriberTransaction.PurchasedAt == default ? fallbackTransaction.PurchasedAt : subscriberTransaction.PurchasedAt,
        };
    }

    private static RevenueCatVerifiedPurchase? MapTransaction(JsonElement item, string productId, List<string> transactionIds)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var transactionId = transactionIds.Count > 0 ? transactionIds[0] : string.Empty;
        if (string.IsNullOrWhiteSpace(transactionId))
        {
            return null;
        }

        var purchasedAt = GetDateTimeOffset(item, "purchase_date")
            ?? GetDateTimeOffset(item, "purchased_at")
            ?? GetDateTimeOffsetFromMilliseconds(item, "purchase_date_ms")
            ?? GetDateTimeOffsetFromMilliseconds(item, "purchased_at_ms")
            ?? DateTimeOffset.UtcNow;

        return new RevenueCatVerifiedPurchase
        {
            ProductId = GetString(item, "product_id") ?? productId,
            TransactionId = transactionId,
            Price = GetDecimal(item, "price") ?? GetDecimal(item, "price_in_purchased_currency") ?? 0m,
            Currency = GetString(item, "currency") ?? GetNestedString(item, "price", "currency") ?? "USD",
            Store = NormalizeStore(GetString(item, "store")),
            Status = GetString(item, "status") ?? "completed",
            PurchasedAt = purchasedAt,
            Metadata = GetMetadata(item),
        };
    }

    private static List<string> GetTransactionIds(JsonElement item)
    {
        var result = new List<string>();
        AddTransactionId(result, GetString(item, "store_transaction_id"));
        AddTransactionId(result, GetString(item, "transaction_id"));
        AddTransactionId(result, GetString(item, "id"));
        return result;
    }

    private static void AddTransactionId(List<string> transactionIds, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var trimmed = value.Trim();
        if (transactionIds.Contains(trimmed, StringComparer.Ordinal))
        {
            return;
        }

        transactionIds.Add(trimmed);
    }

    private static Dictionary<string, string?> GetMetadata(JsonElement item)
    {
        if (!item.TryGetProperty("metadata", out var metadata) || metadata.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        return metadata.EnumerateObject()
            .ToDictionary(
                property => property.Name,
                property => property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : property.Value.ToString(),
                StringComparer.Ordinal);
    }

    private static string? GetSubscriberAttribute(JsonElement subscriber, string key)
    {
        if (!subscriber.TryGetProperty("subscriber_attributes", out var attributes) ||
            attributes.ValueKind != JsonValueKind.Object ||
            !attributes.TryGetProperty(key, out var attribute) ||
            attribute.ValueKind != JsonValueKind.Object ||
            !attribute.TryGetProperty("value", out var value))
        {
            return null;
        }

        var email = value.GetString()?.Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(email) || !email.Contains('@', StringComparison.Ordinal) ? null : email;
    }

    private static bool IsCompleted(RevenueCatVerifiedPurchase purchase)
        => string.IsNullOrWhiteSpace(purchase.Status)
           || purchase.Status.Equals("completed", StringComparison.OrdinalIgnoreCase)
           || purchase.Status.Equals("paid", StringComparison.OrdinalIgnoreCase)
           || purchase.Status.Equals("active", StringComparison.OrdinalIgnoreCase);

    private static RevenueCatPurchaseVerificationResult Failure(string status, string message, HttpStatusCode httpStatusCode)
        => new() { Status = status, Message = message, HttpStatusCode = httpStatusCode };

    private static string? GetString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static decimal? GetDecimal(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDecimal(out var decimalValue) => decimalValue,
            JsonValueKind.String when decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var decimalValue) => decimalValue,
            JsonValueKind.Object when value.TryGetProperty("amount", out var nestedAmount) => nestedAmount.ValueKind switch
            {
                JsonValueKind.Number when nestedAmount.TryGetDecimal(out var decimalValue) => decimalValue,
                JsonValueKind.String when decimal.TryParse(nestedAmount.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var nestedParsed) => nestedParsed,
                _ => null,
            },
            _ => null,
        };
    }

    private static string? GetNestedString(JsonElement element, string propertyName, string nestedPropertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.Object ||
            !value.TryGetProperty(nestedPropertyName, out var nestedValue))
        {
            return null;
        }

        return nestedValue.ValueKind == JsonValueKind.String ? nestedValue.GetString() : null;
    }

    private static DateTimeOffset? GetDateTimeOffset(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;
    }

    private static DateTimeOffset? GetDateTimeOffsetFromMilliseconds(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || !value.TryGetInt64(out var milliseconds))
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
    }

    private static string? NormalizeStore(string? store)
        => string.IsNullOrWhiteSpace(store) ? store : store.Trim().ToUpperInvariant();
}
