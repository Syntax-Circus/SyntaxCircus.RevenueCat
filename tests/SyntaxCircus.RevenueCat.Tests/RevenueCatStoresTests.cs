namespace SyntaxCircus.RevenueCat.Tests;

public class RevenueCatStoresTests
{
    [Theory]
    [InlineData(RevenueCatStores.AppStore, "ios")]
    [InlineData(RevenueCatStores.PlayStore, "android")]
    [InlineData(RevenueCatStores.Stripe, "web")]
    [InlineData(RevenueCatStores.RcBilling, "web")]
    [InlineData("SOMETHING_ELSE", "unknown")]
    [InlineData(null, "unknown")]
    public void ToPlatform_MapsKnownStores(string? store, string expectedPlatform)
        => RevenueCatStores.ToPlatform(store).ShouldBe(expectedPlatform);
}
