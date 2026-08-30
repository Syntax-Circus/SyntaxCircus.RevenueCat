using System.Security.Cryptography;

namespace SyntaxCircus.RevenueCat.Tests;

public class RevenueCatSignatureVerifierTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan DefaultTolerance = TimeSpan.FromSeconds(300);

    private static string ComputeHeader(string body, string secret, DateTimeOffset timestamp)
    {
        var unixTimestamp = timestamp.ToUnixTimeSeconds();
        var payload = Encoding.UTF8.GetBytes($"{unixTimestamp}.{body}");
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var computed = hmac.ComputeHash(payload);
        return $"t={unixTimestamp},v1={Convert.ToHexString(computed).ToLowerInvariant()}";
    }

    private static byte[] Body(string json) => Encoding.UTF8.GetBytes(json);

    [Fact]
    public void Verify_CorrectSignature_WithinTolerance_ReturnsTrue()
    {
        const string body = "{\"event\":{\"id\":\"evt_1\"}}";
        const string secret = "whsec_test_secret";
        var header = ComputeHeader(body, secret, Now);

        RevenueCatSignatureVerifier.Verify(Body(body), header, secret, Now, DefaultTolerance).ShouldBeTrue();
    }

    [Fact]
    public void Verify_TimestampAtToleranceBoundary_ReturnsTrue()
    {
        const string body = "{\"event\":{\"id\":\"evt_1\"}}";
        const string secret = "whsec_test_secret";
        var signedAt = Now - DefaultTolerance;
        var header = ComputeHeader(body, secret, signedAt);

        RevenueCatSignatureVerifier.Verify(Body(body), header, secret, Now, DefaultTolerance).ShouldBeTrue();
    }

    [Fact]
    public void Verify_TamperedBody_ReturnsFalse()
    {
        const string originalBody = "{\"event\":{\"id\":\"evt_1\"}}";
        const string tamperedBody = "{\"event\":{\"id\":\"evt_2\"}}";
        const string secret = "whsec_test_secret";
        var header = ComputeHeader(originalBody, secret, Now);

        RevenueCatSignatureVerifier.Verify(Body(tamperedBody), header, secret, Now, DefaultTolerance).ShouldBeFalse();
    }

    [Fact]
    public void Verify_WrongSecret_ReturnsFalse()
    {
        const string body = "{\"event\":{\"id\":\"evt_1\"}}";
        var header = ComputeHeader(body, "correct-secret", Now);

        RevenueCatSignatureVerifier.Verify(Body(body), header, "wrong-secret", Now, DefaultTolerance).ShouldBeFalse();
    }

    [Fact]
    public void Verify_TimestampOutsideTolerance_ReturnsFalse()
    {
        const string body = "{\"event\":{\"id\":\"evt_1\"}}";
        const string secret = "whsec_test_secret";
        var staleTimestamp = Now - DefaultTolerance - TimeSpan.FromSeconds(1);
        var header = ComputeHeader(body, secret, staleTimestamp);

        RevenueCatSignatureVerifier.Verify(Body(body), header, secret, Now, DefaultTolerance).ShouldBeFalse();
    }

    [Fact]
    public void Verify_FutureTimestampOutsideTolerance_ReturnsFalse()
    {
        const string body = "{\"event\":{\"id\":\"evt_1\"}}";
        const string secret = "whsec_test_secret";
        var futureTimestamp = Now + DefaultTolerance + TimeSpan.FromSeconds(1);
        var header = ComputeHeader(body, secret, futureTimestamp);

        RevenueCatSignatureVerifier.Verify(Body(body), header, secret, Now, DefaultTolerance).ShouldBeFalse();
    }

    [Theory]
    [InlineData("v1=abc")]
    [InlineData("t=1700000000")]
    [InlineData("t=1700000000,t=1700000001,v1=abc")]
    [InlineData("t=1700000000,v1=abc,v1=def")]
    [InlineData("t=1700000000,unknown=1,v1=abc")]
    [InlineData("t=not-a-number,v1=abc")]
    [InlineData("t=-1,v1=abc")]
    [InlineData("garbage")]
    [InlineData("")]
    public void Verify_MalformedHeader_ReturnsFalse(string header)
    {
        const string body = "{\"event\":{\"id\":\"evt_1\"}}";
        RevenueCatSignatureVerifier.Verify(Body(body), header, "secret", Now, DefaultTolerance).ShouldBeFalse();
    }

    [Fact]
    public void Verify_SignatureWrongHexLength_ReturnsFalse()
    {
        const string body = "{\"event\":{\"id\":\"evt_1\"}}";
        RevenueCatSignatureVerifier.Verify(Body(body), "t=1700000000,v1=abcd", "secret", Now, DefaultTolerance).ShouldBeFalse();
    }

    [Fact]
    public void Verify_SignatureNonHex_ReturnsFalse()
    {
        const string body = "{\"event\":{\"id\":\"evt_1\"}}";
        var nonHex = new string('z', 64);
        RevenueCatSignatureVerifier.Verify(Body(body), $"t=1700000000,v1={nonHex}", "secret", Now, DefaultTolerance).ShouldBeFalse();
    }

    [Fact]
    public void Verify_EmptySecret_ReturnsFalse()
    {
        const string body = "{\"event\":{\"id\":\"evt_1\"}}";
        var header = ComputeHeader(body, "whsec_test_secret", Now);

        RevenueCatSignatureVerifier.Verify(Body(body), header, string.Empty, Now, DefaultTolerance).ShouldBeFalse();
    }

    [Fact]
    public void Verify_NegativeTolerance_ReturnsFalse()
    {
        const string body = "{\"event\":{\"id\":\"evt_1\"}}";
        const string secret = "whsec_test_secret";
        var header = ComputeHeader(body, secret, Now);

        RevenueCatSignatureVerifier.Verify(Body(body), header, secret, Now, TimeSpan.FromSeconds(-1)).ShouldBeFalse();
    }
}
