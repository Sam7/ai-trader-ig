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
    private readonly Dictionary<string, string> _lastStateByKey = [];
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

        if (!await PostAsync(slack.WebhookUrl, severity, title, message, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        _lastSentByKey[key] = now;
    }

    public async Task<bool> SendStateChangeAsync(
        string key,
        string stateFingerprint,
        WorkerAlertSeverity severity,
        string title,
        string message,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(stateFingerprint);

        var slack = _options.Slack;
        if (!slack.Enabled || string.IsNullOrWhiteSpace(slack.WebhookUrl))
        {
            return false;
        }

        if (severity < slack.SeverityThreshold)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        if (now - _startedAtUtc < slack.StartupSuppressionWindow && severity < WorkerAlertSeverity.Critical)
        {
            return false;
        }

        if (_lastStateByKey.TryGetValue(key, out var lastState)
            && string.Equals(lastState, stateFingerprint, StringComparison.Ordinal))
        {
            return false;
        }

        if (!await PostAsync(slack.WebhookUrl, severity, title, message, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        _lastStateByKey[key] = stateFingerprint;
        return true;
    }

    private async Task<bool> PostAsync(
        string webhookUrl,
        WorkerAlertSeverity severity,
        string title,
        string message,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, webhookUrl)
        {
            Content = JsonContent.Create(new SlackWebhookPayload($"*{severity}: {title}*\n{message}")),
        };
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        _logger.LogWarning(
            "Slack alert delivery failed with HTTP status {StatusCode}.",
            (int)response.StatusCode);
        return false;
    }

    private sealed record SlackWebhookPayload(string Text);
}
