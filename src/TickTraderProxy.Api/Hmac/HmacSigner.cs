using System.Security.Cryptography;
using System.Text;

namespace TickTraderProxy.Api.Hmac;

/// <summary>
/// Реквизиты HMAC-аутентификации TickTrader Web API.
/// Секрет здесь только для подписи на сервере — никогда не логируется и не уходит на клиент.
/// </summary>
public sealed record HmacCredentials(string Id, string Key, string Secret);

/// <summary>
/// Чистая (без побочных эффектов) реализация HMAC-подписи TickTrader Web API.
///
/// Источник истины — официальная дока SoftFX (секция Q&A на …:8443/api/doc/index):
///   signatureString = timestamp + webApiId + webApiKey + httpMethod + requestUri + body
///   base64          = Base64( HMACSHA256( key = webApiSecret, message = signatureString ) )
///   header          = "HMAC {id}:{key}:{timestamp}:{base64}"
///
/// Кодировка байт — US-ASCII (как getBytes по умолчанию в эталонной Java-реализации SoftFX),
/// единая для message и key. timestamp — Unix-время в миллисекундах.
/// requestUri подписывается ровно в том виде, в каком уходит в запрос (без нормализации).
/// </summary>
public static class HmacSigner
{
    /// <summary>Кодировка байт для message и key. По доке SoftFX — US-ASCII.</summary>
    public static readonly Encoding SignatureEncoding = Encoding.ASCII;

    /// <summary>
    /// Строка-сообщение, над которой считается HMAC.
    /// </summary>
    public static string BuildSignatureString(
        long timestampMs, string id, string key, string httpMethod, string requestUri, string body) =>
        timestampMs.ToString(System.Globalization.CultureInfo.InvariantCulture)
        + id + key + httpMethod + requestUri + body;

    /// <summary>
    /// Base64( HMACSHA256( secret, signatureString ) ).
    /// </summary>
    public static string ComputeSignature(string secret, string signatureString)
    {
        using var hmac = new HMACSHA256(SignatureEncoding.GetBytes(secret));
        var hash = hmac.ComputeHash(SignatureEncoding.GetBytes(signatureString));
        return Convert.ToBase64String(hash);
    }

    /// <summary>
    /// Готовое значение заголовка Authorization: "HMAC {id}:{key}:{timestamp}:{base64}".
    /// </summary>
    public static string BuildAuthorizationHeader(
        HmacCredentials credentials, long timestampMs, string httpMethod, string requestUri, string body)
    {
        var signatureString = BuildSignatureString(
            timestampMs, credentials.Id, credentials.Key, httpMethod, requestUri, body);
        var signature = ComputeSignature(credentials.Secret, signatureString);
        return $"HMAC {credentials.Id}:{credentials.Key}:{timestampMs}:{signature}";
    }
}
