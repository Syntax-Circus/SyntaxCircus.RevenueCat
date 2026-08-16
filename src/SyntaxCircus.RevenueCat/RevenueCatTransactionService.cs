using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace SyntaxCircus.RevenueCat;

public sealed partial class RevenueCatTransactionService(
    HttpClient httpClient,
    IOptions<RevenueCatOptions> revenueCatOptions,
    ILogger<RevenueCatTransactionService> logger) : IRevenueCatTransactionService
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "RevenueCat transaction reconciliation skipped: no RevenueCat API v1-compatible key is configured")]
    private static partial void LogMissingApiKey(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "RevenueCat transaction reconciliation skipped: transactions endpoint is not configured")]
    private static partial void LogMissingEndpoint(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "RevenueCat reconciliation fetched {Count} transactions for window {StartDate} - {EndDate}")]
    private static partial void LogFetchedTransactions(ILogger logger, int count, DateTimeOffset startDate, DateTimeOffset endDate);

    [LoggerMessage(Level = LogLevel.Warning, Message = "RevenueCat reconciliation request failed with HTTP {StatusCode}")]
    private static partial void LogRequestFailed(ILogger logger, int statusCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "RevenueCat reconciliation request failed")]
    private static partial void LogRequestException(ILogger logger, Exception exception);

    public async Task<IReadOnlyList<RevenueCatTransaction>> GetTransactionsAsync(
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        CancellationToken cancellationToken = default)
    {
        var options = revenueCatOptions.Value;
        var credentials = RevenueCatApiKeyResolver.ResolveV1CompatibleCredentials(options);
        if (credentials.Count == 0)
        {
            LogMissingApiKey(logger);
            return [];
        }

        if (string.IsNullOrWhiteSpace(options.TransactionsEndpoint))
        {
            LogMissingEndpoint(logger);
            return [];
        }

        var requestPath = BuildTransactionsPath(options.TransactionsEndpoint, startDate, endDate);

        using var request = new HttpRequestMessage(HttpMethod.Get, requestPath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials[0].ApiKey);

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                LogRequestFailed(logger, (int)response.StatusCode);
                return [];
            }

            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken).ConfigureAwait(false);

            var transactions = ParseTransactions(document.RootElement, startDate, endDate);
            LogFetchedTransactions(logger, transactions.Count, startDate, endDate);
            return transactions;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            LogRequestException(logger, ex);
            return [];
        }
    }

    private static string BuildTransactionsPath(string endpoint, DateTimeOffset startDate, DateTimeOffset endDate)
    {
        var separator = endpoint.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{endpoint}{separator}start_date={Uri.EscapeDataString(startDate.UtcDateTime.ToString("O", CultureInfo.InvariantCulture))}&end_date={Uri.EscapeDataString(endDate.UtcDateTime.ToString("O", CultureInfo.InvariantCulture))}");
    }

    private static List<RevenueCatTransaction> ParseTransactions(JsonElement root, DateTimeOffset startDate, DateTimeOffset endDate)
    {
        var transactions = new List<RevenueCatTransaction>();

        if (TryGetArray(root, "transactions", out var transactionsArray))
        {
            ReadArray(transactionsArray, transactions, startDate, endDate);
        }

        if (TryGetArray(root, "items", out var itemsArray))
        {
            ReadArray(itemsArray, transactions, startDate, endDate);
        }

        if (root.TryGetProperty("subscriber", out var subscriber) &&
            subscriber.TryGetProperty("non_subscriptions", out var nonSubscriptions) &&
            nonSubscriptions.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in nonSubscriptions.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Array)
                {
                    ReadArray(property.Value, transactions, startDate, endDate);
                }
            }
        }

        return [.. transactions
            .Where(transaction => transaction.PurchasedAt >= startDate && transaction.PurchasedAt <= endDate)
            .GroupBy(transaction => transaction.TransactionId, StringComparer.Ordinal)
            .Select(group => group.First())];
    }

    private static bool TryGetArray(JsonElement root, string propertyName, out JsonElement value)
    {
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty(propertyName, out value) &&
            value.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        value = default;
        return false;
    }

    private static void ReadArray(JsonElement array, List<RevenueCatTransaction> destination, DateTimeOffset startDate, DateTimeOffset endDate)
    {
        foreach (var item in array.EnumerateArray())
        {
            if (TryMapTransaction(item, startDate, endDate, out var transaction))
            {
                destination.Add(transaction);
            }
        }
    }

    private static bool TryMapTransaction(JsonElement item, DateTimeOffset startDate, DateTimeOffset endDate, out RevenueCatTransaction transaction)
    {
        var transactionId = GetString(item, "transaction_id") ?? GetString(item, "id") ?? string.Empty;
        var appUserId = GetString(item, "app_user_id") ?? GetString(item, "original_app_user_id") ?? string.Empty;
        var productId = GetString(item, "product_id") ?? GetString(item, "store_product_id") ?? string.Empty;

        var purchasedAt = GetDateTimeOffset(item, "purchase_date")
            ?? GetDateTimeOffset(item, "purchased_at")
            ?? GetDateTimeOffsetFromMilliseconds(item, "purchase_date_ms")
            ?? GetDateTimeOffsetFromMilliseconds(item, "event_timestamp_ms")
            ?? DateTimeOffset.UtcNow;

        if (string.IsNullOrWhiteSpace(transactionId) ||
            string.IsNullOrWhiteSpace(appUserId) ||
            string.IsNullOrWhiteSpace(productId) ||
            purchasedAt < startDate ||
            purchasedAt > endDate)
        {
            transaction = default!;
            return false;
        }

        var price = GetDecimal(item, "price") ?? GetDecimal(item, "amount") ?? 0m;
        var currency = GetString(item, "currency") ?? GetNestedString(item, "price", "currency") ?? "USD";
        var store = NormalizeStore(GetString(item, "store"));
        var status = GetString(item, "status") ?? "completed";

        transaction = new RevenueCatTransaction
        {
            TransactionId = transactionId,
            AppUserId = appUserId,
            ProductId = productId,
            Price = price,
            Currency = currency,
            Store = store,
            Status = status,
            PurchasedAt = purchasedAt,
        };

        return true;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
    }

    private static decimal? GetDecimal(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var decimalValue))
        {
            return decimalValue;
        }

        if (value.ValueKind == JsonValueKind.String &&
            decimal.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("amount", out var nestedAmount))
        {
            if (nestedAmount.ValueKind == JsonValueKind.Number && nestedAmount.TryGetDecimal(out var nestedDecimal))
            {
                return nestedDecimal;
            }

            if (nestedAmount.ValueKind == JsonValueKind.String &&
                decimal.TryParse(nestedAmount.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static string? GetNestedString(JsonElement element, string propertyName, string nestedPropertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.Object ||
            !value.TryGetProperty(nestedPropertyName, out var nestedValue) ||
            nestedValue.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return nestedValue.GetString();
    }

    private static DateTimeOffset? GetDateTimeOffset(JsonElement element, string propertyName)
    {
        var value = GetString(element, propertyName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }

    private static DateTimeOffset? GetDateTimeOffsetFromMilliseconds(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(number);
        }

        if (value.ValueKind == JsonValueKind.String &&
            long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(parsed);
        }

        return null;
    }

    private static string? NormalizeStore(string? store)
        => string.IsNullOrWhiteSpace(store) ? store : store.Trim().ToUpperInvariant();
}
