using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SyntaxCircus.RevenueCat;

public sealed partial class RevenueCatProductCatalogService(
    HttpClient httpClient,
    IOptions<RevenueCatOptions> revenueCatOptions,
    ILogger<RevenueCatProductCatalogService> logger) : IRevenueCatProductCatalogService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "RevenueCat product sync {Operation} succeeded for '{StoreIdentifier}' in app '{AppId}'")]
    private static partial void LogPublishSuccess(ILogger logger, string operation, string storeIdentifier, string appId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "RevenueCat product sync request failed with HTTP {StatusCode}: {Message}")]
    private static partial void LogPublishFailure(ILogger logger, int statusCode, string message);

    public async Task<RevenueCatProductPublishResult> PublishOneTimeProductAsync(
        RevenueCatProductPublishRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.StoreIdentifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DisplayName);

        var options = revenueCatOptions.Value;
        var apiKey = RequireConfiguredValue(options.ProductSyncApiKey, nameof(options.ProductSyncApiKey));
        var projectId = RequireConfiguredValue(options.ProjectId, nameof(options.ProjectId));
        var appIds = ResolveAppIds(options);
        var results = new List<RevenueCatPublishedAppResult>(appIds.Length);

        foreach (var appId in appIds)
        {
            try
            {
                var existing = await FindProductByStoreIdentifierAsync(projectId, appId, request.StoreIdentifier, apiKey, cancellationToken)
                    .ConfigureAwait(false);

                if (existing is null)
                {
                    var created = await CreateProductAsync(projectId, appId, request, apiKey, cancellationToken).ConfigureAwait(false);
                    LogPublishSuccess(logger, "create", request.StoreIdentifier, appId);
                    results.Add(created with { Created = true });
                    continue;
                }

                var updated = await UpdateProductAsync(projectId, appId, existing.ProductId, request, apiKey, cancellationToken).ConfigureAwait(false);
                LogPublishSuccess(logger, "update", request.StoreIdentifier, appId);
                results.Add(updated with { Created = false });
            }
            catch (RevenueCatProductSyncException ex)
            {
                throw new RevenueCatProductSyncException($"RevenueCat product sync failed for app '{appId}': {ex.Message}", ex.StatusCode);
            }
        }

        return new RevenueCatProductPublishResult(request.StoreIdentifier, results);
    }

    private async Task<RevenueCatProductReference?> FindProductByStoreIdentifierAsync(
        string projectId,
        string appId,
        string storeIdentifier,
        string apiKey,
        CancellationToken cancellationToken)
    {
        string? nextPagePath = BuildProductsListPath(projectId, appId, null);

        while (!string.IsNullOrWhiteSpace(nextPagePath))
        {
            using var request = CreateRequest(HttpMethod.Get, nextPagePath, apiKey);
            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                ThrowSyncException(response.StatusCode, payload);
            }

            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    var currentStoreIdentifier = GetString(item, "store_identifier");
                    if (!string.Equals(currentStoreIdentifier, storeIdentifier, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var productId = GetString(item, "id");
                    if (string.IsNullOrWhiteSpace(productId))
                    {
                        continue;
                    }

                    return new RevenueCatProductReference(productId, currentStoreIdentifier!);
                }
            }

            nextPagePath = document.RootElement.TryGetProperty("next_page", out var nextPage) && nextPage.ValueKind == JsonValueKind.String
                ? nextPage.GetString()
                : null;
        }

        return null;
    }

    private async Task<RevenueCatPublishedAppResult> CreateProductAsync(
        string projectId,
        string appId,
        RevenueCatProductPublishRequest request,
        string apiKey,
        CancellationToken cancellationToken)
    {
        using var body = CreateJsonContent(new
        {
            store_identifier = request.StoreIdentifier,
            app_id = appId,
            type = "non_consumable",
            display_name = request.DisplayName,
            title = request.DisplayName,
        });

        using var httpRequest = CreateRequest(HttpMethod.Post, $"projects/{projectId}/products", apiKey, body);
        using var response = await httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            ThrowSyncException(response.StatusCode, payload);
        }

        return ParsePublishedAppResult(payload, appId);
    }

    private async Task<RevenueCatPublishedAppResult> UpdateProductAsync(
        string projectId,
        string appId,
        string productId,
        RevenueCatProductPublishRequest request,
        string apiKey,
        CancellationToken cancellationToken)
    {
        using var body = CreateJsonContent(new { display_name = request.DisplayName });

        using var httpRequest = CreateRequest(HttpMethod.Post, $"projects/{projectId}/products/{productId}", apiKey, body);
        using var response = await httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            ThrowSyncException(response.StatusCode, payload);
        }

        // Pass appId through explicitly rather than relying on the response's app_id field —
        // the update endpoint doesn't always echo it back.
        return ParsePublishedAppResult(payload, appId);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string requestPath, string apiKey, HttpContent? body = null)
    {
        var request = new HttpRequestMessage(method, CreateRequestUri(requestPath));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = body;
        return request;
    }

    private Uri CreateRequestUri(string requestPath)
    {
        if (Uri.TryCreate(requestPath, UriKind.Absolute, out var absoluteUri))
        {
            return absoluteUri;
        }

        var baseAddress = httpClient.BaseAddress;
        if (baseAddress is null)
        {
            return new Uri(requestPath, UriKind.Relative);
        }

        if (requestPath.StartsWith('/'))
        {
            var origin = new Uri(baseAddress.GetLeftPart(UriPartial.Authority));
            return new Uri(origin, requestPath);
        }

        var normalizedBase = baseAddress.AbsoluteUri.EndsWith('/') ? baseAddress : new Uri(baseAddress.AbsoluteUri + "/", UriKind.Absolute);
        return new Uri(normalizedBase, requestPath);
    }

    private static StringContent CreateJsonContent<T>(T payload)
        => new(JsonSerializer.Serialize(payload, SerializerOptions), Encoding.UTF8, "application/json");

    private static string BuildProductsListPath(string projectId, string appId, string? nextPagePath)
    {
        if (!string.IsNullOrWhiteSpace(nextPagePath))
        {
            return nextPagePath;
        }

        return $"projects/{Uri.EscapeDataString(projectId)}/products?app_id={Uri.EscapeDataString(appId)}&limit=100";
    }

    private static string RequireConfiguredValue(string? value, string optionName)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        throw new InvalidOperationException($"RevenueCat product sync requires '{optionName}' to be configured.");
    }

    private void ThrowSyncException(HttpStatusCode statusCode, string payload)
    {
        var message = TryGetErrorMessage(payload) ?? $"RevenueCat product sync failed with HTTP {(int)statusCode}.";
        LogPublishFailure(logger, (int)statusCode, message);
        throw new RevenueCatProductSyncException(message, (int)statusCode);
    }

    private static RevenueCatPublishedAppResult ParsePublishedAppResult(string payload, string appId)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        var productId = GetString(root, "id")
            ?? throw new RevenueCatProductSyncException("RevenueCat product sync response did not include a product id.");
        return new RevenueCatPublishedAppResult(appId, productId, Created: false);
    }

    private static string[] ResolveAppIds(RevenueCatOptions options)
    {
        var configured = options.ProductSyncAppIds
            .Where(appId => !string.IsNullOrWhiteSpace(appId))
            .Select(appId => appId.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (configured.Length > 0)
        {
            return configured;
        }

        return [RequireConfiguredValue(options.ProductSyncAppId, nameof(options.ProductSyncAppId))];
    }

    private static string? TryGetErrorMessage(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            return GetString(document.RootElement, "message");
        }
        catch (JsonException)
        {
            return payload.Trim();
        }
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.GetRawText();
    }

    private sealed record RevenueCatProductReference(string ProductId, string StoreIdentifier);
}
