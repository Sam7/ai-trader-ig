using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Trading.Automation.Configuration;
using Trading.Automation.Health;

public sealed class SlackAlertServiceTests
{
    [Fact]
    public async Task SendAsync_ShouldPostOnceWithinCooldownWithoutLeakingWebhookUrl()
    {
        var handler = new RecordingHandler();
        var webhookUrl = "https://hooks.slack.test/services/secret";
        var service = new SlackAlertService(
            new HttpClient(handler),
            Options.Create(new AlertingOptions
            {
                Slack = new SlackAlertOptions
                {
                    Enabled = true,
                    WebhookUrl = webhookUrl,
                    Cooldown = TimeSpan.FromMinutes(30),
                    StartupSuppressionWindow = TimeSpan.Zero,
                },
            }),
            NullLogger<SlackAlertService>.Instance);

        await service.SendAsync("memory", WorkerAlertSeverity.Warning, "High memory", "RSS is elevated.");
        await service.SendAsync("memory", WorkerAlertSeverity.Warning, "High memory", "RSS is still elevated.");

        handler.RequestBodies.Should().ContainSingle();
        handler.RequestBodies[0].Should().Contain("High memory");
        handler.RequestBodies[0].Should().NotContain(webhookUrl);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestBodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
