using System.Text.Json.Serialization;

namespace Ig.Trading.Sdk.Models;

public sealed record SessionRequest(
    [property: JsonPropertyName("identifier")] string Identifier,
    [property: JsonPropertyName("password")] string Password,
    [property: JsonPropertyName("encryptedPassword")] bool EncryptedPassword = false);

public sealed record EncryptionKeyResponse(
    [property: JsonPropertyName("encryptionKey")] string EncryptionKey,
    [property: JsonPropertyName("timeStamp")] long TimeStamp);

public sealed record SwitchAccountRequest(
    [property: JsonPropertyName("accountId")] string AccountId,
    [property: JsonPropertyName("defaultAccount")] bool DefaultAccount = true);

public sealed record SessionResponse(
    [property: JsonPropertyName("currentAccountId")] string? CurrentAccountId,
    [property: JsonPropertyName("lightstreamerEndpoint")] string? LightstreamerEndpoint,
    [property: JsonPropertyName("accounts")] IReadOnlyList<SessionAccount>? Accounts);

public sealed record SessionAccount(
    [property: JsonPropertyName("accountId")] string AccountId,
    [property: JsonPropertyName("accountType")] string? AccountType,
    [property: JsonPropertyName("accountName")] string? AccountName);
