using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

public sealed class SpectreTypeRegistrar : ITypeRegistrar
{
    private readonly IServiceCollection _services;

    public SpectreTypeRegistrar(IServiceCollection services)
    {
        _services = services;
    }

    public ITypeResolver Build()
    {
        return new SpectreTypeResolver(_services.BuildServiceProvider());
    }

    public void Register(Type service, Type implementation)
    {
        _services.AddSingleton(service, implementation);
    }

    public void RegisterInstance(Type service, object implementation)
    {
        _services.AddSingleton(service, implementation);
    }

    public void RegisterLazy(Type service, Func<object> factory)
    {
        _services.AddSingleton(service, _ => factory());
    }
}

public sealed class SpectreTypeResolver : ITypeResolver, IDisposable
{
    private readonly ServiceProvider _provider;

    public SpectreTypeResolver(ServiceProvider provider)
    {
        _provider = provider;
    }

    public object? Resolve(Type? type)
    {
        if (type is null)
        {
            return null;
        }

        if (type == typeof(CommandSettings))
        {
            return new EmptyCommandSettings();
        }

        return _provider.GetService(type)
               ?? (type.IsAbstract || type.IsInterface ? null : ActivatorUtilities.CreateInstance(_provider, type));
    }

    public void Dispose()
    {
        // Spectre disposes the resolver after command execution.
        // Keep the provider alive so post-run error handling can still write to the injected console.
    }
}
