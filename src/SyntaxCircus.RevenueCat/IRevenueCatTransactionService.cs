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
}

public interface IRevenueCatTransactionService
{
    Task<IReadOnlyList<RevenueCatTransaction>> GetTransactionsAsync(
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        CancellationToken cancellationToken = default);
}
