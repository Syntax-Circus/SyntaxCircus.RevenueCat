using System.Security.Cryptography;

namespace SyntaxCircus.RevenueCat.Tests;

public class RevenueCatSignatureVerifierTests
{
    private static string ComputeSignature(string body, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var computed = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
        return Convert.ToHexString(computed).ToLowerInvariant();
    }

    [Fact]
    public void Verify_CorrectSignature_ReturnsTrue()
    {
        const string body = "{\"event\":{\"id\":\"evt_1\"}}";
        const string secret = "whsec_test_secret";
        var signature = ComputeSignature(body, secret);

        RevenueCatSignatureVerifier.Verify(body, signature, secret).ShouldBeTrue();
    }

    [Fact]
    public void Verify_SignatureIsUppercaseHex_StillMatches()
    {
        const string body = "{\"event\":{\"id\":\"evt_1\"}}";
        const string secret = "whsec_test_secret";
        var signature = ComputeSignature(body, secret).ToUpperInvariant();

        RevenueCatSignatureVerifier.Verify(body, signature, secret).ShouldBeTrue();
    }

    [Fact]
    public void Verify_TamperedBody_ReturnsFalse()
    {
        const string originalBody = "{\"event\":{\"id\":\"evt_1\"}}";
        const string tamperedBody = "{\"event\":{\"id\":\"evt_2\"}}";
        const string secret = "whsec_test_secret";
        var signature = ComputeSignature(originalBody, secret);

        RevenueCatSignatureVerifier.Verify(tamperedBody, signature, secret).ShouldBeFalse();
    }

    [Fact]
    public void Verify_WrongSecret_ReturnsFalse()
    {
        const string body = "{\"event\":{\"id\":\"evt_1\"}}";
        var signature = ComputeSignature(body, "correct-secret");

        RevenueCatSignatureVerifier.Verify(body, signature, "wrong-secret").ShouldBeFalse();
    }

    [Fact]
    public void Verify_GarbageSignature_ReturnsFalse()
    {
        RevenueCatSignatureVerifier.Verify("{}", "not-a-real-signature", "secret").ShouldBeFalse();
    }

    [Fact]
    public void Verify_EmptySignature_ReturnsFalse()
    {
        const string body = "{\"event\":{\"id\":\"evt_1\"}}";
        RevenueCatSignatureVerifier.Verify(body, string.Empty, "secret").ShouldBeFalse();
    }

    [Fact]
    public void Verify_NullRawBody_ThrowsArgumentNullException()
        => Should.Throw<ArgumentNullException>(() => RevenueCatSignatureVerifier.Verify(null!, "sig", "secret"));

    [Fact]
    public void Verify_NullSignatureHeader_ThrowsArgumentNullException()
        => Should.Throw<ArgumentNullException>(() => RevenueCatSignatureVerifier.Verify("{}", null!, "secret"));

    [Fact]
    public void Verify_NullSecret_ThrowsArgumentNullException()
        => Should.Throw<ArgumentNullException>(() => RevenueCatSignatureVerifier.Verify("{}", "sig", null!));
}
