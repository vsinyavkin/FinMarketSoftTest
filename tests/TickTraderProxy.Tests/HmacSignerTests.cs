using System.Security.Cryptography;
using System.Text;
using TickTraderProxy.Api.Hmac;

namespace TickTraderProxy.Tests;

public class HmacSignerTests
{
    private static readonly HmacCredentials Creds = new("api-id", "api-key", "my-secret");
    private const long TimestampMs = 1_700_000_000_000;
    private const string Uri = "https://demonstrationwebapi.soft-fx.eu:8443/api/v2/account";

    [Fact]
    public void BuildSignatureString_concatenates_in_documented_order()
    {
        // Act: строим message-строку для подписи.
        var s = HmacSigner.BuildSignatureString(TimestampMs, "api-id", "api-key", "GET", Uri, body: "");

        // Assert: порядок ровно timestamp + id + key + method + uri + body.
        Assert.Equal("1700000000000api-idapi-keyGET" + Uri, s);
    }

    [Fact]
    public void ComputeSignature_matches_pinned_reference_vector()
    {
        // Arrange: эталон посчитан независимо (PowerShell, HMACSHA256, US-ASCII) — пиннит кодировку и порядок.
        const string expected = "2lRx0rklRByzgWNfI8HcHKriOtCMLGFJlCWhk2m4f5I=";

        // Act: считаем подпись штатным сигнером.
        var signatureString = HmacSigner.BuildSignatureString(TimestampMs, "api-id", "api-key", "GET", Uri, "");
        var signature = HmacSigner.ComputeSignature(Creds.Secret, signatureString);

        // Assert: совпадает с пиннингованным вектором.
        Assert.Equal(expected, signature);
    }

    [Fact]
    public void ComputeSignature_uses_us_ascii_encoding()
    {
        // Arrange: воспроизводим алгоритм независимо с явной US-ASCII кодировкой.
        var signatureString = HmacSigner.BuildSignatureString(TimestampMs, "api-id", "api-key", "GET", Uri, "");
        using var hmac = new HMACSHA256(Encoding.ASCII.GetBytes(Creds.Secret));
        var expected = Convert.ToBase64String(hmac.ComputeHash(Encoding.ASCII.GetBytes(signatureString)));

        // Act + Assert: штатный сигнер использует ту же кодировку → результат совпадает.
        Assert.Equal(expected, HmacSigner.ComputeSignature(Creds.Secret, signatureString));
    }

    [Fact]
    public void ComputeSignature_is_deterministic()
    {
        // Arrange: одна и та же message-строка.
        var s = HmacSigner.BuildSignatureString(TimestampMs, "api-id", "api-key", "GET", Uri, "");

        // Act + Assert: два вызова с тем же секретом дают идентичную подпись.
        Assert.Equal(HmacSigner.ComputeSignature(Creds.Secret, s), HmacSigner.ComputeSignature(Creds.Secret, s));
    }

    [Fact]
    public void ComputeSignature_changes_with_secret()
    {
        // Arrange: одна и та же message-строка.
        var s = HmacSigner.BuildSignatureString(TimestampMs, "api-id", "api-key", "GET", Uri, "");

        // Act + Assert: разные секреты дают разные подписи (секрет реально влияет на HMAC).
        Assert.NotEqual(HmacSigner.ComputeSignature("secret-a", s), HmacSigner.ComputeSignature("secret-b", s));
    }

    [Fact]
    public void BuildAuthorizationHeader_has_documented_shape()
    {
        // Act: строим готовый заголовок Authorization.
        var header = HmacSigner.BuildAuthorizationHeader(Creds, TimestampMs, "GET", Uri, "");

        // Assert: форма "HMAC {id}:{key}:{timestamp}:{base64}".
        Assert.StartsWith("HMAC ", header);
        var payload = header["HMAC ".Length..];
        var parts = payload.Split(':');
        Assert.Equal(4, parts.Length);
        Assert.Equal("api-id", parts[0]);
        Assert.Equal("api-key", parts[1]);
        Assert.Equal("1700000000000", parts[2]);
        Assert.Equal("2lRx0rklRByzgWNfI8HcHKriOtCMLGFJlCWhk2m4f5I=", parts[3]);
    }

    [Fact]
    public void BuildSignatureString_includes_post_body_verbatim()
    {
        // Arrange: тело POST-ордера.
        const string body = "{\"Type\":\"Market\",\"Side\":\"Buy\",\"Symbol\":\"EURUSD\",\"Amount\":1000}";

        // Act: строим message-строку для POST.
        var s = HmacSigner.BuildSignatureString(TimestampMs, "api-id", "api-key", "POST", Uri, body);

        // Assert: тело входит в подпись дословно и в конце строки.
        Assert.EndsWith(body, s);
        Assert.Contains("POST" + Uri + body, s);
    }
}
