using System.Text.Json.Serialization;

namespace Ig.Trading.Sdk.Models;

public sealed record AccountsResponse(
    [property: JsonPropertyName("accounts")] IReadOnlyList<AccountItem>? Accounts);

public sealed record AccountItem(
    [property: JsonPropertyName("accountId")] string AccountId,
    [property: JsonPropertyName("accountName")] string? AccountName,
    [property: JsonPropertyName("accountAlias")] string? AccountAlias,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("accountType")] string? AccountType,
    [property: JsonPropertyName("preferred")] bool Preferred,
    [property: JsonPropertyName("balance")] AccountBalance? Balance,
    [property: JsonPropertyName("currency")] string? Currency,
    [property: JsonPropertyName("canTransferFrom")] bool CanTransferFrom,
    [property: JsonPropertyName("canTransferTo")] bool CanTransferTo);

public sealed record AccountBalance(
    [property: JsonPropertyName("balance")] decimal Balance,
    [property: JsonPropertyName("deposit")] decimal Deposit,
    [property: JsonPropertyName("profitLoss")] decimal ProfitLoss,
    [property: JsonPropertyName("available")] decimal Available);
