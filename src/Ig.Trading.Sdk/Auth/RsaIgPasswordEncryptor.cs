using System.Security.Cryptography;
using System.Text;
using Ig.Trading.Sdk.Models;

namespace Ig.Trading.Sdk.Auth;

internal sealed class RsaIgPasswordEncryptor : IIgPasswordEncryptor
{
    public string Encrypt(string password, EncryptionKeyResponse encryptionKey)
    {
        using var rsa = RSA.Create();
        rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(encryptionKey.EncryptionKey), out _);

        var payload = $"{password}|{encryptionKey.TimeStamp.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        var encrypted = rsa.Encrypt(Encoding.UTF8.GetBytes(payload), RSAEncryptionPadding.Pkcs1);
        return Convert.ToBase64String(encrypted);
    }
}
