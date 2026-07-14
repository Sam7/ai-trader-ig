using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Trading.AI.DailyBriefing;
using Trading.AI.Prompts;
using Trading.Automation.Configuration;
using Trading.Automation.Execution;
using Trading.Strategy.DayPlanning;
using Trading.Strategy.Persistence;
using Trading.Strategy.Shared;

public sealed class IntradayOpportunityScanServiceTests
{
    [Fact]
    public async Task RunAsync_when_daily_plan_creation_fails_should_not_prepare_a_review()
    {
        var store = new InMemoryTradingDayStore();
        var planner = new ThrowingTradingDayPlanner();
        var preparation = new FakePreparationService();
        var service = CreateService(store, planner, preparation);

        var result = await service.RunAsync(
            new DateOnly(2026, 7, 3),
            DateTimeOffset.Parse("2026-07-03T01:00:00Z"));

        result.Should().BeNull();
        planner.PlanRequests.Should().ContainSingle();
        preparation.PrepareCalls.Should().Be(0);
    }

    [Fact]
    public async Task PrepareAsync_should_delegate_without_creating_a_daily_plan()
    {
        var store = new InMemoryTradingDayStore();
        var planner = new ThrowingTradingDayPlanner();
        var preparation = new FakePreparationService();
        var service = CreateService(store, planner, preparation);

        var result = await service.PrepareAsync(
            new DateOnly(2026, 7, 3),
            DateTimeOffset.Parse("2026-07-03T01:00:00Z"));

        result.Should().BeNull();
        planner.PlanRequests.Should().BeEmpty();
        preparation.PrepareCalls.Should().Be(1);
    }

    [Fact]
    public async Task SubmitAsync_should_load_analyze_and_coordinate_the_prepared_review()
    {
        var events = new List<string>();
        var document = TestData.CreatePreparationDocument();
        var execution = TestData.CreateExecution();
        var expected = TestData.CreateSubmitResult(document, execution);
        var preparation = new FakePreparationService(document, events);
        var analysis = new FakeAnalysisService(execution, events);
        var coordinator = new FakeDecisionCoordinator(expected, events);
        var service = CreateService(
            new InMemoryTradingDayStore(),
            new ThrowingTradingDayPlanner(),
            preparation,
            analysis,
            coordinator);

        var result = await service.SubmitAsync("prepared.json");

        result.Should().BeSameAs(expected);
        events.Should().Equal("load", "analyze", "coordinate");
    }

    private static IntradayOpportunityScanService CreateService(
        ITradingDayStore store,
        ITradingDayPlanner planner,
        IIntradayOpportunityPreparationService preparation,
        IIntradayOpportunityAnalysisService? analysis = null,
        IIntradayOpportunityDecisionCoordinator? coordinator = null)
        => new(
            CreateEnsureService(store, planner),
            new IntradayOpportunityScanGate(),
            preparation,
            analysis ?? new FakeAnalysisService(),
            coordinator ?? new FakeDecisionCoordinator(),
            Options.Create(new AutomationOptions()),
            NullLogger<IntradayOpportunityScanService>.Instance);

    private static DailyPlanEnsureService CreateEnsureService(
        ITradingDayStore store,
        ITradingDayPlanner planner)
    {
        var services = new ServiceCollection();
        services.AddSingleton(planner);
        services.AddSingleton(Options.Create(new AutomationOptions()));
        services.AddSingleton<ILogger<DailyBriefingPlanService>>(NullLogger<DailyBriefingPlanService>.Instance);
        services.AddTransient<DailyBriefingPlanService>();
        var provider = services.BuildServiceProvider();

        return new DailyPlanEnsureService(
            store,
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new AutomationOptions()),
            NullLogger<DailyPlanEnsureService>.Instance);
    }

    private sealed class ThrowingTradingDayPlanner : ITradingDayPlanner
    {
        public List<TradingDayRequest> PlanRequests { get; } = [];

        public Task<TradingDayPlan> PlanAsync(
            TradingDayRequest request,
            CancellationToken cancellationToken = default)
        {
            PlanRequests.Add(request);
            throw new InvalidOperationException("Daily plan failed.");
        }
    }

    private sealed class FakePreparationService(
        IntradayOpportunityPreparationDocument? document = null,
        List<string>? events = null) : IIntradayOpportunityPreparationService
    {
        public int PrepareCalls { get; private set; }

        public Task<IntradayOpportunityPreparationDocument?> PrepareAsync(
            DateOnly tradingDate,
            DateTimeOffset requestedAtUtc,
            CancellationToken cancellationToken = default)
        {
            PrepareCalls++;
            return Task.FromResult(document);
        }

        public Task<IntradayOpportunityPreparationDocument> LoadAsync(
            string path,
            CancellationToken cancellationToken = default)
        {
            events?.Add("load");
            return Task.FromResult(document!);
        }
    }

    private sealed class FakeAnalysisService(
        IntradayOpportunityReviewExecution? execution = null,
        List<string>? events = null) : IIntradayOpportunityAnalysisService
    {
        public PromptContractProvenance Contract { get; } = new(
            "intraday-opportunity-review",
            "1",
            new string('a', 64),
            "1",
            new string('b', 64));

        public string RenderRequestText(IntradayOpportunityReviewRequest request) => "request";

        public Task<IntradayOpportunityReviewExecution> AnalyzeAsync(
            IntradayOpportunityPreparationDocument prepared,
            CancellationToken cancellationToken = default)
        {
            events?.Add("analyze");
            return Task.FromResult(execution!);
        }
    }

    private sealed class FakeDecisionCoordinator(
        IntradayOpportunitySubmitResult? result = null,
        List<string>? events = null) : IIntradayOpportunityDecisionCoordinator
    {
        public Task<IntradayOpportunitySubmitResult> CoordinateAsync(
            IntradayOpportunityPreparationDocument prepared,
            IntradayOpportunityReviewExecution execution,
            CancellationToken cancellationToken = default)
        {
            events?.Add("coordinate");
            return Task.FromResult(result!);
        }
    }

    private static class TestData
    {
        public static IntradayOpportunityPreparationDocument CreatePreparationDocument()
        {
            var request = new IntradayOpportunityReviewRequest(
                new DateOnly(2026, 7, 3),
                DateTimeOffset.Parse("2026-07-03T00:00:00Z"),
                DateTimeOffset.Parse("2026-07-03T01:00:00Z"),
                1,
                "Australia/Sydney",
                null!,
                [],
                DateTimeOffset.Parse("2026-07-03T01:00:00Z"));
            var artifact = new ArtifactReference(Path.GetFullPath("prepared.json"), new Uri(Path.GetFullPath("prepared.json")).AbsoluteUri);
            return new IntradayOpportunityPreparationDocument(
                request.TradingDate,
                request.RequestedAtUtc,
                "intraday-opportunity-review",
                request,
                "request",
                [],
                [],
                artifact,
                artifact);
        }

        public static IntradayOpportunityReviewExecution CreateExecution()
        {
            var batch = new IntradayOpportunityBatch(
                new DateOnly(2026, 7, 3),
                DateTimeOffset.Parse("2026-07-03T01:00:00Z"),
                DateTimeOffset.Parse("2026-07-03T00:00:00Z"),
                DateTimeOffset.Parse("2026-07-03T01:00:00Z"),
                [],
                []);
            return new IntradayOpportunityReviewExecution(batch, "request", "envelope.json", "structured.json", []);
        }

        public static IntradayOpportunitySubmitResult CreateSubmitResult(
            IntradayOpportunityPreparationDocument document,
            IntradayOpportunityReviewExecution execution)
        {
            var artifact = new ArtifactReference(Path.GetFullPath("artifact.json"), new Uri(Path.GetFullPath("artifact.json")).AbsoluteUri);
            var review = new IntradayOpportunityReviewResult(
                document.TradingDate,
                [],
                [],
                TradingExecutionMode.Disabled,
                [],
                null,
                new IntradayCandidateDecisionSummary(0, 0, 0, 0, 0),
                document.RequestedAtUtc,
                "No candidates.");
            return new IntradayOpportunitySubmitResult(
                document,
                new IntradayOpportunityExecutionArtifacts(artifact, artifact, []),
                execution.Batch,
                review,
                null);
        }
    }
}
