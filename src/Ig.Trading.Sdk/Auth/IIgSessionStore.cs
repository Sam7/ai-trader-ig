namespace Ig.Trading.Sdk.Auth;

public interface IIgSessionStore
{
    IgSessionContext Current { get; }

    void Set(IgSessionContext context);

    void Clear();
}
