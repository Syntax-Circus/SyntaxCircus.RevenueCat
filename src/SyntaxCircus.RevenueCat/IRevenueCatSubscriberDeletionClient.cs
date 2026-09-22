namespace SyntaxCircus.RevenueCat;

public interface IRevenueCatSubscriberDeletionClient
{
    /// <summary>
    /// Permanently deletes a RevenueCat subscriber (<c>DELETE v1/subscribers/{app_user_id}</c>) - use
    /// when a user closes their account. Requires a v1 secret API key
    /// (<see cref="RevenueCatOptions.ApiKey"/>); RevenueCat rejects this endpoint for a public/app API
    /// key. Idempotent: a subscriber that's already gone (or never existed) resolves to
    /// <see cref="RevenueCatSubscriberDeletionResult.NotFound"/> instead of throwing. Any other
    /// non-success response throws <see cref="HttpRequestException"/> with
    /// <see cref="HttpRequestException.StatusCode"/> set, so callers can decide whether to retry
    /// (e.g. on 429/5xx).
    /// </summary>
    Task<RevenueCatSubscriberDeletionResult> DeleteSubscriberAsync(string appUserId, CancellationToken cancellationToken = default);
}
