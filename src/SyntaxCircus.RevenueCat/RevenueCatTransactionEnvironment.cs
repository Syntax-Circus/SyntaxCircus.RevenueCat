namespace SyntaxCircus.RevenueCat;

/// <summary>
/// A store transaction's real environment - distinct from which app-backend a client selected, and
/// from how the client was distributed. See <see cref="RevenueCatOptions.ExpectedTransactionEnvironment"/>.
/// </summary>
public enum RevenueCatTransactionEnvironment
{
    Sandbox,
    Production,
}

/// <summary>
/// Checks a transaction's real store environment against a deployment's expected one - the backstop
/// against a sandbox/test transaction being recorded as production revenue (or vice versa)
/// regardless of what any client believes its selected backend to be. A deployment opts in by
/// setting <see cref="RevenueCatOptions.ExpectedTransactionEnvironment"/>; leaving it unset (the
/// default) means every check here matches, preserving existing single-environment consumers.
/// </summary>
public static class RevenueCatTransactionEnvironmentMatcher
{
    private const string SandboxRawValue = "SANDBOX";
    private const string ProductionRawValue = "PRODUCTION";

    /// <summary>
    /// True if <paramref name="rawEnvironment"/> (a webhook event's raw <c>environment</c> field, e.g.
    /// <see cref="RevenueCatEvent.Environment"/>) matches <paramref name="expected"/>, or if
    /// <paramref name="expected"/> is <see langword="null"/> (enforcement disabled) or
    /// <paramref name="rawEnvironment"/> is unset (can't be verified, so it isn't rejected here).
    /// </summary>
    public static bool MatchesWebhookEnvironment(RevenueCatTransactionEnvironment? expected, string? rawEnvironment)
    {
        if (expected is null || string.IsNullOrWhiteSpace(rawEnvironment))
        {
            return true;
        }

        var expectedRawValue = expected.Value == RevenueCatTransactionEnvironment.Sandbox ? SandboxRawValue : ProductionRawValue;
        return string.Equals(rawEnvironment, expectedRawValue, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True if <paramref name="isSandbox"/> (from <see cref="RevenueCatVerifiedPurchase.IsSandbox"/>)
    /// matches <paramref name="expected"/>, or if <paramref name="expected"/> is
    /// <see langword="null"/> (enforcement disabled) or <paramref name="isSandbox"/> is
    /// <see langword="null"/> (unknown - the caller-configurable transactions-fallback endpoint
    /// doesn't guarantee this field, so it isn't rejected here).
    /// </summary>
    public static bool MatchesVerifiedPurchase(RevenueCatTransactionEnvironment? expected, bool? isSandbox)
    {
        if (expected is null || isSandbox is null)
        {
            return true;
        }

        return (expected.Value == RevenueCatTransactionEnvironment.Sandbox) == isSandbox.Value;
    }
}
