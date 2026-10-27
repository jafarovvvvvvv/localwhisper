using System.Security.Cryptography;
using System.Text;

namespace LocalWhisper.Services;

/// <summary>
/// Шифрование секретов ключом текущей учётной записи Windows (DPAPI).
///
/// Ровно то же, что делали <c>encrypt_secret</c>/<c>decrypt_secret</c> в
/// питоновской версии, и тот же формат на диске: base64 от блоба
/// <c>CryptProtectData</c> в поле <c>openai_api_key_enc</c>. Ключ, записанный
/// старым приложением, читается новым и наоборот.
/// </summary>
public static class Secrets
{
    public static string? Protect(string? plain)
    {
        if (string.IsNullOrEmpty(plain)) return null;
        try
        {
            var bytes = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(bytes);
        }
        catch (Exception e)
        {
            Log.Error("secrets", "не зашифровать ключ", e);
            return null;
        }
    }

    public static string? Unprotect(string? encoded)
    {
        if (string.IsNullOrEmpty(encoded)) return null;
        try
        {
            var bytes = ProtectedData.Unprotect(
                Convert.FromBase64String(encoded), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            // Файл перенесли с другой машины или профиль пересоздали. Секрет
            // потерян — но это не повод не запуститься: ключ введут заново.
            return null;
        }
    }
}
