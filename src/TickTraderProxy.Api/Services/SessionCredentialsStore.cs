using Microsoft.AspNetCore.DataProtection;
using TickTraderProxy.Api.Hmac;

namespace TickTraderProxy.Api.Services;

/// <summary>
/// Хранит креды в серверной сессии. Секрет шифруется <see cref="IDataProtector"/> и
/// никогда не попадает на клиент, в логи или URL. Id/Key — нечувствительны, хранятся как есть.
/// </summary>
public sealed class SessionCredentialsStore(IDataProtectionProvider protectionProvider)
{
    private const string IdKey = "tt.id";
    private const string KeyKey = "tt.key";
    private const string SecretKey = "tt.secret.protected";

    private readonly IDataProtector _protector = protectionProvider.CreateProtector("TickTrader.Secret.v1");

    public void Save(ISession session, HmacCredentials credentials)
    {
        session.SetString(IdKey, credentials.Id);
        session.SetString(KeyKey, credentials.Key);
        session.SetString(SecretKey, _protector.Protect(credentials.Secret));
    }

    public HmacCredentials? Load(ISession session)
    {
        var id = session.GetString(IdKey);
        var key = session.GetString(KeyKey);
        var protectedSecret = session.GetString(SecretKey);
        if (id is null || key is null || protectedSecret is null)
            return null;

        return new HmacCredentials(id, key, _protector.Unprotect(protectedSecret));
    }

    public void Clear(ISession session)
    {
        session.Remove(IdKey);
        session.Remove(KeyKey);
        session.Remove(SecretKey);
    }
}
