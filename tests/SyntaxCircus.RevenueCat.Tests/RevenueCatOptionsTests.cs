namespace SyntaxCircus.RevenueCat.Tests;

public class RevenueCatOptionsTests
{
    [Fact]
    public void Defaults_MatchExpectedValues()
    {
        var options = new RevenueCatOptions();

        options.RequireWebhookSecret.ShouldBeTrue();
        options.ApiBaseUrl.ShouldBe("https://api.revenuecat.com/");
        options.ProductSyncApiBaseUrl.ShouldBe("https://api.revenuecat.com/v2/");
        options.TransactionsEndpoint.ShouldBe("v1/transactions");
        options.ProductSyncAppIds.ShouldBeEmpty();
        options.ApiKey.ShouldBeNull();
        options.PublicApiKey.ShouldBeNull();
        options.WebhookSecret.ShouldBeNull();
    }

    [Fact]
    public void SectionName_IsRevenueCat()
        => RevenueCatOptions.SectionName.ShouldBe("RevenueCat");
}
