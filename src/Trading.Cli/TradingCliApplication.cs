using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;
using Trading.Abstractions;

public sealed class TradingCliApplication
{
    private readonly IServiceCollection _services;
    private readonly IAnsiConsole _console;
    private readonly TradingCliRenderer _renderer;

    public TradingCliApplication(IServiceCollection services, IAnsiConsole console)
    {
        _services = services;
        _console = console;
        _renderer = new TradingCliRenderer(console);
    }

    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var commandApp = new CommandApp(new SpectreTypeRegistrar(_services));
        commandApp.Configure(Configure);

        if (args.Length == 0)
        {
            await commandApp.RunAsync(["--help"], cancellationToken);
            return 1;
        }

        try
        {
            var exitCode = await commandApp.RunAsync(args, cancellationToken);
            return exitCode == 0 ? 0 : 1;
        }
        catch (TradingGatewayException exception)
        {
            _renderer.WriteTradingError(exception);
            return 2;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _renderer.WriteCancellation();
            return 130;
        }
        catch (CliUsageException exception)
        {
            _renderer.WriteUsageError(exception.Message);
            return 1;
        }
        catch (CommandParseException exception)
        {
            _renderer.WriteUsageError(exception.Message);
            return 1;
        }
        catch (CommandRuntimeException exception) when (exception.InnerException is null)
        {
            _renderer.WriteUsageError(exception.Message);
            return 1;
        }
        catch (CommandRuntimeException exception)
        {
            _renderer.WriteUnexpectedError(exception.InnerException ?? exception);
            return 99;
        }
        catch (Exception exception)
        {
            _renderer.WriteUnexpectedError(exception);
            return 99;
        }
    }

    private void Configure(IConfigurator configurator)
    {
        configurator.ConfigureConsole(_console);
        configurator.PropagateExceptions();
        configurator.CancellationExitCode(130);
        configurator.SetApplicationName("trading");
        configurator.AddCommand<AuthenticateCommand>("auth");

        configurator.AddBranch("trades", trades =>
        {
            trades.AddCommand<BuyTradeCommand>("buy");
            trades.AddCommand<SellTradeCommand>("sell");
        });

        configurator.AddBranch("working", working =>
        {
            working.AddCommand<ListWorkingOrdersCommand>("list");
            working.AddCommand<CreateWorkingOrderCommand>("create");
            working.AddCommand<UpdateWorkingOrderCommand>("update");
            working.AddCommand<CancelWorkingOrderCommand>("cancel");
        });

        configurator.AddBranch("positions", positions =>
        {
            positions.AddCommand<ListPositionsCommand>("list");
            positions.AddCommand<ClosePositionCommand>("close");
            positions.AddCommand<UpdatePositionCommand>("update");
        });

        configurator.AddBranch("markets", markets =>
        {
            markets.AddCommand<SearchMarketsCommand>("search");
            markets.AddCommand<BrowseMarketsCommand>("browse");
            markets.AddCommand<ShowPricesCommand>("prices");
            markets.AddCommand<RenderMarketChartCommand>("chart");
        });

        configurator.AddBranch("orders", orders =>
        {
            orders.AddCommand<ListOrdersCommand>("list");
            orders.AddCommand<ShowOrderStatusCommand>("status");
        });

        configurator.AddExample(["auth"]);
        configurator.AddExample(["trades", "buy", "--instrument", "IX.D.SPTRD.DAILY.IP", "--size", "1"]);
        configurator.AddExample(["markets", "search", "--query", "VIX"]);
        configurator.AddExample(["markets", "browse"]);
        configurator.AddExample(["markets", "prices", "--instrument", "CC.D.VIX.UMA.IP", "--resolution", "hour", "--max", "10"]);
        configurator.AddExample(["markets", "chart", "--instrument", "CC.D.VIX.UMA.IP", "--resolution", "hour", "--max", "50", "--output", "artifacts\\vix-chart.png", "--style", "candlestick", "--sma", "20,50", "--bollinger", "20"]);
        configurator.AddExample(["positions", "list"]);
        configurator.AddExample(["positions", "update", "--deal-id", "DIAAAAAAA", "--stop-level", "1", "--limit-level", "100"]);
        configurator.AddExample(["positions", "close", "--deal-id", "DIAAAAAAA"]);
        configurator.AddExample(["orders", "status", "--deal-reference", "spike-..."]);
    }
}
