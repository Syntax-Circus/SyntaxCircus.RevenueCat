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
    /// In V1, fetches transactions from <see cref="RevenueCatOptions.TransactionsEndpoint"/>, which
    /// requires a custom aggregation proxy. In V2, scans paginated project customers and their
    /// one-time purchases, then applies the inclusive purchase-date window locally. Prefer
    /// <see cref="GetTransactionsForCandidatesAsync"/> when candidate customer IDs are available.
    /// V2 request, response, or pagination failures throw rather than returning partial results.
    /// </summary>
    Task<IReadOnlyList<RevenueCatTransaction>> GetTransactionsAsync(
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reconciles transactions for caller-supplied customer IDs. V1 queries
    /// <c>GET v1/subscribers/{app_user_id}</c> and aggregates <c>non_subscriptions</c>.
    /// V2 queries each customer's paginated one-time purchases, resolves store product identifiers,
    /// and applies the inclusive purchase-date window locally. V2 request, response, or pagination
    /// failures throw rather than returning partial results.
    /// </summary>
    Task<IReadOnlyList<RevenueCatTransaction>> GetTransactionsForCandidatesAsync(
        IReadOnlyCollection<string> candidateAppUserIds,
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        CancellationToken cancellationToken = default);
}
