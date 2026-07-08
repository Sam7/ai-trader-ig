using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Trading.Execution;

public interface IExecutionDealReferenceFactory
{
    string CreateOpenReference(string decisionId);

    string CreateReference(string operationId, ExecutionOperationKind operationKind);
}

public sealed partial class ExecutionDealReferenceFactory : IExecutionDealReferenceFactory
{
    public string CreateOpenReference(string decisionId)
        => CreateReference(decisionId, ExecutionOperationKind.MarketOpen);

    public string CreateReference(string operationId, ExecutionOperationKind operationKind)
    {
        if (string.IsNullOrWhiteSpace(operationId))
        {
            throw new ArgumentException("Operation ID is required.", nameof(operationId));
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(operationId));
        var reference = $"{ResolvePrefix(operationKind)}{Convert.ToHexString(hash)[..20]}";
        if (!DealReferenceRegex().IsMatch(reference))
        {
            throw new InvalidOperationException($"Generated deal reference '{reference}' is not broker-safe.");
        }

        return reference;
    }

    private static string ResolvePrefix(ExecutionOperationKind operationKind)
        => operationKind switch
        {
            ExecutionOperationKind.MarketOpen => "ATOPEN",
            ExecutionOperationKind.PositionClose => "ATCLOSE",
            ExecutionOperationKind.PositionUpdate => "ATUPD",
            ExecutionOperationKind.WorkingOrderCreate => "ATWOC",
            ExecutionOperationKind.WorkingOrderUpdate => "ATWOU",
            ExecutionOperationKind.WorkingOrderCancel => "ATWOD",
            _ => throw new ArgumentOutOfRangeException(nameof(operationKind), operationKind, "Unsupported execution operation kind."),
        };

    [GeneratedRegex("^[A-Z0-9]{1,30}$")]
    private static partial Regex DealReferenceRegex();
}
