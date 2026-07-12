using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trading.Automation.Configuration;

namespace Trading.Automation.Health;

public sealed class SlackAlertService
{
    private readonly HttpClient _httpClient;
    private readonly AlertingOptions _options;
    private readonly ILogger<SlackAlertService> _logger;
    private readonly Dictionary<string, DateTimeOffset> _lastSentByKey = [];
    private readonly DateTimeOffset _startedAtUtc = DateTimeOffset.UtcNow;

    public SlackAlertService(
        HttpClient httpClient,
        IOptions<AlertingOptions> options,
        ILogger<SlackAlertService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendAsync(
        string key,
        WorkerAlertSeverity severity,
        string title,
        string message,
        CancellationToken cancellationToken = default)
    {
        var slack = _options.Slack;
        if (!slack.Enabled || string.IsNullOrWhiteSpace(slack.WebhookUrl))
        {
            return;
        }

        if (severity < slack.SeverityThreshold)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (now - _startedAtUtc < slack.StartupSuppressionWindow && severity < WorkerAlertSeverity.Critical)
        {
            return;
        }

        if (_lastSentByKey.TryGetValue(key, out var lastSent)
            && now - lastSent < slack.Cooldown)
        {
            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, slack.WebhookUrl)
        {
            Content = JsonContent.Create(new SlackWebhookPayload($"*{severity}: {title}*\n{message}")),
        };
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Slack alert delivery failed with HTTP status {StatusCode}.",
                (int)response.StatusCode);
            return;
        }

        _lastSentByKey[key] = now;
    }

    private sealed record SlackWebhookPayload(string Text);
}
