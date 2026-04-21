namespace SmartCollect.Application.Services;

using System.Collections.Concurrent;
using SmartCollect.Application.Interfaces;

public class InMemoryDispatchExecutionGuard : IDispatchExecutionGuard
{
    private readonly ConcurrentDictionary<Guid, int> _locks = new();

    public IDisposable BlockTenant(Guid tenantId)
    {
        _locks.AddOrUpdate(tenantId, 1, (_, count) => count + 1);
        return new Releaser(_locks, tenantId);
    }

    public bool IsTenantBlocked(Guid tenantId)
        => _locks.TryGetValue(tenantId, out var count) && count > 0;

    private sealed class Releaser : IDisposable
    {
        private readonly ConcurrentDictionary<Guid, int> _locks;
        private readonly Guid _tenantId;
        private int _disposed;

        public Releaser(ConcurrentDictionary<Guid, int> locks, Guid tenantId)
        {
            _locks = locks;
            _tenantId = tenantId;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
                return;

            _locks.AddOrUpdate(_tenantId, 0, (_, count) => Math.Max(0, count - 1));

            if (_locks.TryGetValue(_tenantId, out var count) && count == 0)
                _locks.TryRemove(_tenantId, out _);
        }
    }
}
