namespace SyntaxCircus.RevenueCat;

public interface IRevenueCatSubscriberAliasClient
{
    /// <summary>Aliases an anonymous RevenueCat app user ID to a canonical (identified) one — e.g. after login.</summary>
    Task CreateAliasAsync(string canonicalAppUserId, string anonymousAppUserId, CancellationToken cancellationToken = default);
}
