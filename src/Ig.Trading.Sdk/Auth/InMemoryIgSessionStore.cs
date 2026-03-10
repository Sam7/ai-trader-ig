using System.Threading;

namespace Ig.Trading.Sdk.Auth;

public sealed class InMemoryIgSessionStore : IIgSessionStore
{
    private readonly ReaderWriterLockSlim _lock = new();
    private IgSessionContext _context = new(null, null, null, null);

    public IgSessionContext Current
    {
        get
        {
            _lock.EnterReadLock();
            try
            {
                return _context;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }
    }

    public void Set(IgSessionContext context)
    {
        _lock.EnterWriteLock();
        try
        {
            _context = context;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public void Clear()
    {
        _lock.EnterWriteLock();
        try
        {
            _context = new IgSessionContext(null, null, null, null);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }
}
