using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Reflection;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models;
using Trading.Automation.Configuration;
using Trading.Automation.Execution;
using Trading.Automation.Scheduling;

public sealed class ScheduleInitializerTests
{
    [Fact]
    public async Task DailyBriefingStartAsync_ShouldRegisterConfiguredCron()
    {
        var manager = FakeCronTickerManager.Succeeding();
        var initializer = new DailyBriefingScheduleInitializer(
            manager,
            Options.Create(new AutomationOptions
            {
                Enabled = true,
                DailyBriefCron = "0 5 8 * * *",
                Timezone = "Australia/Melbourne",
            }),
            NullLogger<DailyBriefingScheduleInitializer>.Instance);

        await initializer.StartAsync(CancellationToken.None);

        var ticker = manager.Added.Should().ContainSingle().Subject;
        ticker.Function.Should().Be(DailyBriefingConstants.JobName);
        ticker.Expression.Should().Be("0 5 8 * * *");
    }

    [Fact]
    public async Task DailyBriefingStartAsync_ShouldFailWhenTickerQRejectsSchedule()
    {
        var manager = FakeCronTickerManager.Failing(new InvalidOperationException("duplicate function"));
        var initializer = new DailyBriefingScheduleInitializer(
            manager,
            Options.Create(new AutomationOptions { Enabled = true, DailyBriefCron = "0 5 8 * * *" }),
            NullLogger<DailyBriefingScheduleInitializer>.Instance);

        var action = () => initializer.StartAsync(CancellationToken.None);

        await action.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*DailyBriefingPlan*");
    }

    [Fact]
    public async Task IntradayOpportunityStartAsync_ShouldRegisterConfiguredCron()
    {
        var manager = FakeCronTickerManager.Succeeding();
        var initializer = new IntradayOpportunityScheduleInitializer(
            manager,
            Options.Create(new AutomationOptions
            {
                Enabled = true,
                Timezone = "Australia/Melbourne",
                IntradayOpportunities = new IntradayOpportunityScanOptions
                {
                    Enabled = true,
                    Cron = "0 */5 * * * *",
                },
            }),
            NullLogger<IntradayOpportunityScheduleInitializer>.Instance);

        await initializer.StartAsync(CancellationToken.None);

        var ticker = manager.Added.Should().ContainSingle().Subject;
        ticker.Function.Should().Be(IntradayOpportunityConstants.JobName);
        ticker.Expression.Should().Be("0 */5 * * * *");
    }

    [Fact]
    public async Task IntradayOpportunityStartAsync_ShouldFailWhenTickerQRejectsSchedule()
    {
        var manager = FakeCronTickerManager.Failing(new InvalidOperationException("invalid cron"));
        var initializer = new IntradayOpportunityScheduleInitializer(
            manager,
            Options.Create(new AutomationOptions
            {
                Enabled = true,
                IntradayOpportunities = new IntradayOpportunityScanOptions
                {
                    Enabled = true,
                    Cron = "bad cron",
                },
            }),
            NullLogger<IntradayOpportunityScheduleInitializer>.Instance);

        var action = () => initializer.StartAsync(CancellationToken.None);

        await action.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*IntradayOpportunityScan*");
    }

    private sealed class FakeCronTickerManager : ICronTickerManager<CronTickerEntity>
    {
        private readonly Func<CronTickerEntity, TickerResult<CronTickerEntity>> _addResultFactory;

        private FakeCronTickerManager(Func<CronTickerEntity, TickerResult<CronTickerEntity>> addResultFactory)
        {
            _addResultFactory = addResultFactory;
        }

        public List<CronTickerEntity> Added { get; } = [];

        public static FakeCronTickerManager Succeeding()
            => new(entity =>
            {
                entity.Id = Guid.NewGuid();
                return CreateResult(entity, 1);
            });

        public static FakeCronTickerManager Failing(Exception exception)
            => new(_ => CreateResult(exception));

        public Task<TickerResult<CronTickerEntity>> AddAsync(
            CronTickerEntity entity,
            CancellationToken cancellationToken = default)
        {
            Added.Add(entity);
            return Task.FromResult(_addResultFactory(entity));
        }

        public Task<TickerResult<List<CronTickerEntity>>> AddBatchAsync(
            List<CronTickerEntity> entities,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<TickerResult<CronTickerEntity>> UpdateAsync(
            CronTickerEntity entity,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<TickerResult<List<CronTickerEntity>>> UpdateBatchAsync(
            List<CronTickerEntity> entities,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<TickerResult<CronTickerEntity>> DeleteAsync(
            Guid id,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<TickerResult<CronTickerEntity>> DeleteBatchAsync(
            List<Guid> ids,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        private static TickerResult<CronTickerEntity> CreateResult(params object[] args)
            => (TickerResult<CronTickerEntity>)Activator.CreateInstance(
                typeof(TickerResult<CronTickerEntity>),
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                args,
                CultureInfo.InvariantCulture)!;
    }
}
