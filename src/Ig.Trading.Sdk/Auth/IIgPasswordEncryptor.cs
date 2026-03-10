using Ig.Trading.Sdk.Models;

namespace Ig.Trading.Sdk.Auth;

internal interface IIgPasswordEncryptor
{
    string Encrypt(string password, EncryptionKeyResponse encryptionKey);
}
