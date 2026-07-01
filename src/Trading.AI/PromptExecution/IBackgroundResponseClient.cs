using Microsoft.Extensions.AI;

namespace Trading.AI.PromptExecution;

internal interface IBackgroundResponseClient
{
    Task<BackgroundResponseResult> CreateAsync(
        PromptInvocation invocation,
        IReadOnlyList<ChatMessage> messages,
        ChatOptions options,
        CancellationToken cancellationToken);

    Task<BackgroundResponseResult> GetAsync(
        string modelId,
        string responseId,
        ChatOptions options,
        CancellationToken cancellationToken);

    Task CancelAsync(
        string modelId,
        string responseId,
        CancellationToken cancellationToken);
}

internal sealed record BackgroundResponseResult(
    string? ResponseId,
    string Status,
    ChatResponse? Response,
    string? ErrorMessage = null);
