namespace Ig.Trading.Sdk.Auth;

public sealed record IgSessionContext(
    string? Cst,
    string? SecurityToken,
    string? CurrentAccountId,
    DateTimeOffset? AuthenticatedAtUtc);
