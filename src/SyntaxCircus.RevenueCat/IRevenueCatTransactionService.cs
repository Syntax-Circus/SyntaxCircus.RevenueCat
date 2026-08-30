namespace SyntaxCircus.RevenueCat;

public sealed record RevenueCatTransaction
{
    public string TransactionId { get; init; } = string.Empty;
    public string AppUserId { get; init; } = string.Empty;
    public string ProductId { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public string Currency { get; init; } = "USD";
    public string? Store { get; init; }
    public string Status { get; init; } = "completed";
    public DateTimeOffset PurchasedAt { get; init; }
    public Dictionary<string, string?> Metadata { get; init; } = [];
}

public interface IRevenueCatTransactionService
{
    /// <summary>
    /// Fetches transactions from <see cref="RevenueCatOptions.TransactionsEndpoint"/>. RevenueCat's
    /// own REST API has no bulk "list transactions in a date range" endpoint, so this only returns
    /// results if <see cref="RevenueCatOptions.TransactionsEndpoint"/> has been pointed at a custom
    /// aggregation proxy you control. Most consumers should use
    /// <see cref="GetTransactionsForCandidatesAsync"/> instead.
    /// </summary>
    Task<IReadOnlyList<RevenueCatTransaction>> GetTransactionsAsync(
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reconciles transactions for a caller-supplied set of candidate app_user_ids by querying
    /// <c>GET v1/subscribers/{app_user_id}</c> per id and aggregating <c>non_subscriptions</c>. Use
    /// this when your own store can supply candidate ids — RevenueCat's REST API has no bulk
    /// "list transactions in a date range" endpoint, so this is the production-realistic
    /// reconciliation path against RevenueCat's real API.
    /// </summary>
    Task<IReadOnlyList<RevenueCatTransaction>> GetTransactionsForCandidatesAsync(
        IReadOnlyCollection<string> candidateAppUserIds,
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        CancellationToken cancellationToken = default);
}
