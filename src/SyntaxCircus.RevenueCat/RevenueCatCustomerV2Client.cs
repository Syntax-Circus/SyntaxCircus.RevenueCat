using System.Net;
using System.Text.Json;
using System.Net.Http.Headers;

namespace SyntaxCircus.RevenueCat;

// Shared by the existing public clients so their direct-construction signatures remain compatible.
internal sealed class RevenueCatCustomerV2Client
{
    private const int MaximumPages = 1000;
    private readonly HttpClient httpClient;
    private readonly RevenueCatOptions options;
    private readonly Uri projectUri;
    private readonly string apiKey;
    private readonly Dictionary<string, (string StoreId, string AppId)> products = new(StringComparer.Ordinal);

    public RevenueCatCustomerV2Client(HttpClient httpClient, RevenueCatOptions options)
    {
        this.httpClient = httpClient;
        this.options = options;
        if (string.IsNullOrWhiteSpace(options.ApiKey) || string.IsNullOrWhiteSpace(options.ProjectId))
        {
            throw new InvalidOperationException("RevenueCat V2 customer operations require RevenueCat:ApiKey and RevenueCat:ProjectId.");
        }

        if (!Uri.TryCreate(options.CustomerApiBaseUrl, UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(baseUri.UserInfo) ||
            !string.IsNullOrEmpty(baseUri.Query) || !string.IsNullOrEmpty(baseUri.Fragment))
        {
            throw new InvalidOperationException("RevenueCat:CustomerApiBaseUrl must be an absolute HTTPS base URL without credentials, query, or fragment.");
        }

        apiKey = options.ApiKey.Trim();
        projectUri = new Uri(new Uri(baseUri.AbsoluteUri.TrimEnd('/') + "/"), $"projects/{Uri.EscapeDataString(options.ProjectId.Trim())}/");
    }

    public async Task<RevenueCatSubscriberDeletionResult> DeleteAsync(string appUserId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Delete, CustomerUri(appUserId), cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return RevenueCatSubscriberDeletionResult.NotFound;
        }

        EnsureSuccess(response);
        return RevenueCatSubscriberDeletionResult.Deleted;
    }

    public async Task<IReadOnlyList<RevenueCatTransaction>> GetTransactionsAsync(
        IReadOnlyCollection<string>? candidateIds, DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken)
    {
        var ids = candidateIds is null
            ? (await ReadCollectionAsync(new Uri(projectUri, "customers"), cancellationToken).ConfigureAwait(false))
                .Select(item => RequiredString(item, "id")).ToArray()
            : candidateIds;
        var transactions = new List<RevenueCatTransaction>();
        foreach (var id in ids.Distinct(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
            var purchases = await GetPurchasesAsync(id, cancellationToken).ConfigureAwait(false);
            transactions.AddRange(purchases.Where(purchase => purchase.PurchasedAt >= start && purchase.PurchasedAt <= end));
        }

        return transactions.DistinctBy(purchase => (
            purchase.Metadata["revenuecat_app_id"], purchase.Store, purchase.Metadata["revenuecat_environment"], purchase.TransactionId)).ToArray();
    }

    public async Task<IReadOnlyList<RevenueCatTransaction>> GetPurchasesAsync(string appUserId, CancellationToken cancellationToken)
    {
        var items = await ReadCollectionAsync(new Uri(CustomerUri(appUserId).AbsoluteUri + "/purchases"), cancellationToken, allowNotFound: true).ConfigureAwait(false);
        var result = new List<RevenueCatTransaction>();
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var productId = RequiredString(item, "product_id");
            var product = await GetProductAsync(productId, cancellationToken).ConfigureAwait(false);
            var transactionId = RequiredIdentifier(item, "store_purchase_identifier");
            var environment = RequiredString(item, "environment");
            var status = RequiredString(item, "status");
            if (environment is not ("sandbox" or "production") || status is not ("owned" or "refunded"))
            {
                throw InvalidResponse();
            }

            if (!item.TryGetProperty("purchased_at", out var timestamp) || timestamp.ValueKind != JsonValueKind.Number || !timestamp.TryGetInt64(out var milliseconds) ||
                !item.TryGetProperty("revenue_in_usd", out var revenue) || revenue.ValueKind != JsonValueKind.Object ||
                RequiredString(revenue, "currency") != "USD" || !revenue.TryGetProperty("gross", out var gross) ||
                gross.ValueKind != JsonValueKind.Number || !gross.TryGetDecimal(out var price) || price < 0)
            {
                throw InvalidResponse();
            }

            DateTimeOffset purchasedAt;
            try { purchasedAt = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds); }
            catch (ArgumentOutOfRangeException) { throw InvalidResponse(); }

            var store = RequiredString(item, "store");
            var customerId = RequiredString(item, "customer_id");
            if (options.CustomerAppIds.Length != 0 && !options.CustomerAppIds.Contains(product.AppId, StringComparer.Ordinal))
            {
                continue;
            }

            result.Add(new RevenueCatTransaction
            {
                TransactionId = transactionId, AppUserId = appUserId, ProductId = product.StoreId,
                Price = price, Currency = "USD", Store = store.ToUpperInvariant(),
                Status = status == "owned" ? "completed" : "refunded", PurchasedAt = purchasedAt,
                Metadata = new(StringComparer.Ordinal)
                {
                    ["revenuecat_app_id"] = product.AppId, ["revenuecat_environment"] = environment,
                    ["revenuecat_product_id"] = productId, ["revenuecat_customer_id"] = customerId,
                },
            });
        }

        return result;
    }

    private async Task<(string StoreId, string AppId)> GetProductAsync(string productId, CancellationToken cancellationToken)
    {
        if (products.TryGetValue(productId, out var cached)) { return cached; }
        using var response = await SendAsync(HttpMethod.Get, new Uri(projectUri, $"products/{Uri.EscapeDataString(productId)}"), cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response);
        using var document = await ReadDocumentAsync(response, cancellationToken).ConfigureAwait(false);
        var product = (RequiredString(document.RootElement, "store_identifier"), RequiredString(document.RootElement, "app_id"));
        products.Add(productId, product);
        return product;
    }

    private async Task<List<JsonElement>> ReadCollectionAsync(Uri collection, CancellationToken cancellationToken, bool allowNotFound = false)
    {
        var result = new List<JsonElement>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var page = new Uri(collection.AbsoluteUri + "?limit=100");
        for (var count = 0; count < MaximumPages; count++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsSameCollection(collection, page) || !visited.Add(page.AbsoluteUri))
            {
                throw new HttpRequestException("RevenueCat returned unsafe or cyclic V2 pagination.");
            }

            using var response = await SendAsync(HttpMethod.Get, page, cancellationToken).ConfigureAwait(false);
            if (allowNotFound && count == 0 && response.StatusCode == HttpStatusCode.NotFound) { return result; }
            EnsureSuccess(response);
            using var document = await ReadDocumentAsync(response, cancellationToken).ConfigureAwait(false);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            {
                throw InvalidResponse();
            }

            result.AddRange(items.EnumerateArray().Select(item => item.Clone()));
            if (!root.TryGetProperty("next_page", out var next) || next.ValueKind == JsonValueKind.Null) { return result; }
            if (next.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(next.GetString()) ||
                !Uri.TryCreate(page, next.GetString(), out var nextUri)) { throw InvalidResponse(); }
            page = nextUri;
        }

        throw new HttpRequestException("RevenueCat V2 pagination exceeded the page limit.");
    }

    private static bool IsSameCollection(Uri collection, Uri page)
        => page.Scheme == collection.Scheme && page.Host == collection.Host && page.Port == collection.Port &&
           string.Equals(page.AbsolutePath, collection.AbsolutePath, StringComparison.Ordinal) &&
           string.IsNullOrEmpty(page.UserInfo) && string.IsNullOrEmpty(page.Fragment);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, Uri uri, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        // DI disables redirects. Also reject a redirected final response from caller-owned clients.
        if (response.RequestMessage?.RequestUri is { } finalUri && finalUri != uri)
        {
            response.Dispose();
            throw new HttpRequestException("RevenueCat V2 redirects are not supported.");
        }

        return response;
    }

    private Uri CustomerUri(string id) => new(projectUri, $"customers/{Uri.EscapeDataString(id)}");
    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"RevenueCat V2 request failed with HTTP {(int)response.StatusCode}.", null, response.StatusCode);
        }
    }

    private static async Task<JsonDocument> ReadDocumentAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException) { throw InvalidResponse(); }
    }

    private static string RequiredString(JsonElement item, string property)
        => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(property, out var value) &&
            value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()! : throw InvalidResponse();

    private static string RequiredIdentifier(JsonElement item, string property)
    {
        if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty(property, out var value))
        {
            if (value.ValueKind == JsonValueKind.Number && value.TryGetUInt64(out _)) { return value.GetRawText(); }
            if (value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())) { return value.GetString()!; }
        }

        throw InvalidResponse();
    }

    private static HttpRequestException InvalidResponse() => new("RevenueCat returned a malformed or unsupported V2 response.");
}
