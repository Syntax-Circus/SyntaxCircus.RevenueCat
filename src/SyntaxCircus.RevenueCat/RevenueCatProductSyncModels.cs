namespace SyntaxCircus.RevenueCat;

public sealed record RevenueCatProductPublishRequest(
    string StoreIdentifier,
    string DisplayName);

public sealed record RevenueCatProductPublishResult(
    string StoreIdentifier,
    IReadOnlyList<RevenueCatPublishedAppResult> PublishedApps);

public sealed record RevenueCatPublishedAppResult(
    string AppId,
    string ProductId,
    bool Created);

public sealed class RevenueCatProductSyncException : Exception
{
    public RevenueCatProductSyncException(string message, int? statusCode = null)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public int? StatusCode { get; }
}

public interface IRevenueCatProductCatalogService
{
    Task<RevenueCatProductPublishResult> PublishOneTimeProductAsync(
        RevenueCatProductPublishRequest request,
        CancellationToken cancellationToken = default);
}
