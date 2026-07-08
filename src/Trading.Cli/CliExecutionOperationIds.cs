internal static class CliExecutionOperationIds
{
    public static string Resolve(string? operationId)
        => string.IsNullOrWhiteSpace(operationId)
            ? $"manual-{Guid.NewGuid():N}"
            : operationId.Trim();
}
