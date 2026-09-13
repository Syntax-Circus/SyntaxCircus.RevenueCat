namespace SyntaxCircus.RevenueCat.Tests;

public class RevenueCatTransactionEnvironmentMatcherTests
{
    [Fact]
    public void MatchesWebhookEnvironment_ExpectedNull_AlwaysMatches()
    {
        RevenueCatTransactionEnvironmentMatcher.MatchesWebhookEnvironment(null, "PRODUCTION").ShouldBeTrue();
        RevenueCatTransactionEnvironmentMatcher.MatchesWebhookEnvironment(null, "SANDBOX").ShouldBeTrue();
        RevenueCatTransactionEnvironmentMatcher.MatchesWebhookEnvironment(null, null).ShouldBeTrue();
    }

    [Fact]
    public void MatchesWebhookEnvironment_RawEnvironmentUnset_Matches()
    {
        RevenueCatTransactionEnvironmentMatcher.MatchesWebhookEnvironment(RevenueCatTransactionEnvironment.Production, null).ShouldBeTrue();
        RevenueCatTransactionEnvironmentMatcher.MatchesWebhookEnvironment(RevenueCatTransactionEnvironment.Production, "").ShouldBeTrue();
    }

    [Theory]
    [InlineData(RevenueCatTransactionEnvironment.Production, "PRODUCTION", true)]
    [InlineData(RevenueCatTransactionEnvironment.Production, "production", true)]
    [InlineData(RevenueCatTransactionEnvironment.Production, "SANDBOX", false)]
    [InlineData(RevenueCatTransactionEnvironment.Sandbox, "SANDBOX", true)]
    [InlineData(RevenueCatTransactionEnvironment.Sandbox, "PRODUCTION", false)]
    public void MatchesWebhookEnvironment_ComparesCaseInsensitively(RevenueCatTransactionEnvironment expected, string rawEnvironment, bool shouldMatch)
    {
        RevenueCatTransactionEnvironmentMatcher.MatchesWebhookEnvironment(expected, rawEnvironment).ShouldBe(shouldMatch);
    }

    [Fact]
    public void MatchesVerifiedPurchase_ExpectedNull_AlwaysMatches()
    {
        RevenueCatTransactionEnvironmentMatcher.MatchesVerifiedPurchase(null, true).ShouldBeTrue();
        RevenueCatTransactionEnvironmentMatcher.MatchesVerifiedPurchase(null, false).ShouldBeTrue();
        RevenueCatTransactionEnvironmentMatcher.MatchesVerifiedPurchase(null, null).ShouldBeTrue();
    }

    [Fact]
    public void MatchesVerifiedPurchase_IsSandboxUnknown_Matches()
    {
        RevenueCatTransactionEnvironmentMatcher.MatchesVerifiedPurchase(RevenueCatTransactionEnvironment.Production, null).ShouldBeTrue();
        RevenueCatTransactionEnvironmentMatcher.MatchesVerifiedPurchase(RevenueCatTransactionEnvironment.Sandbox, null).ShouldBeTrue();
    }

    [Theory]
    [InlineData(RevenueCatTransactionEnvironment.Production, false, true)]
    [InlineData(RevenueCatTransactionEnvironment.Production, true, false)]
    [InlineData(RevenueCatTransactionEnvironment.Sandbox, true, true)]
    [InlineData(RevenueCatTransactionEnvironment.Sandbox, false, false)]
    public void MatchesVerifiedPurchase_ComparesExpectedAgainstIsSandboxFlag(RevenueCatTransactionEnvironment expected, bool isSandbox, bool shouldMatch)
    {
        RevenueCatTransactionEnvironmentMatcher.MatchesVerifiedPurchase(expected, isSandbox).ShouldBe(shouldMatch);
    }
}
