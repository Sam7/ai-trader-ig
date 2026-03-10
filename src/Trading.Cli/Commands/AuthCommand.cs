using Spectre.Console.Cli;
using Trading.Abstractions;

public sealed class AuthenticateCommand : AsyncCommand<EmptyCommandSettings>
{
    private readonly ITradingGateway _gateway;
    private readonly TradingCliRenderer _renderer;

    public AuthenticateCommand(ITradingGateway gateway, TradingCliRenderer renderer)
    {
        _gateway = gateway;
        _renderer = renderer;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, EmptyCommandSettings settings, CancellationToken cancellationToken)
    {
        var session = await _gateway.AuthenticateAsync(cancellationToken);
        _renderer.WriteAuthentication(session);
        return 0;
    }
}
