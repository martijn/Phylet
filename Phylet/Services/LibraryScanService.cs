using Phylet.Data.Library;

namespace Phylet.Services;

public sealed class LibraryScanService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<LibraryScanService> logger) : BackgroundService
{
    private readonly SemaphoreSlim _signal = new(0, int.MaxValue);
    private readonly Lock _stateLock = new();
    private LibraryScanStatus _status = new(false, true, null, null, null);
    private bool _scanRequested = true;

    public LibraryScanStatus Current
    {
        get
        {
            lock (_stateLock)
            {
                return _status;
            }
        }
    }

    public void RequestScan()
    {
        var shouldSignal = false;

        lock (_stateLock)
        {
            if (_scanRequested)
            {
                return;
            }

            _scanRequested = true;
            _status = _status with { IsQueued = true };
            shouldSignal = !_status.IsInProgress;
        }

        if (shouldSignal)
        {
            _signal.Release();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _signal.Release();

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await _signal.WaitAsync(stoppingToken);
                while (TryStartPendingScan(out _))
                {
                    await RunSingleScanAsync(stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown.
        }
    }

    private bool TryStartPendingScan(out DateTime startedUtc)
    {
        lock (_stateLock)
        {
            if (!_scanRequested)
            {
                startedUtc = default;
                return false;
            }

            _scanRequested = false;
            startedUtc = timeProvider.GetUtcNow().UtcDateTime;
            _status = _status with
            {
                IsInProgress = true,
                IsQueued = false,
                StartedUtc = startedUtc,
                LastError = null
            };

            return true;
        }
    }

    private async Task RunSingleScanAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var scanner = scope.ServiceProvider.GetRequiredService<LibraryScanner>();
            await scanner.ScanAsync(cancellationToken);

            lock (_stateLock)
            {
                _status = _status with
                {
                    IsInProgress = false,
                    LastCompletedUtc = timeProvider.GetUtcNow().UtcDateTime,
                    LastError = null
                };
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Library scan failed");

            lock (_stateLock)
            {
                _status = _status with
                {
                    IsInProgress = false,
                    LastError = ex.Message
                };
            }
        }
        finally
        {
            var shouldSignal = false;

            lock (_stateLock)
            {
                shouldSignal = _scanRequested;
                if (shouldSignal)
                {
                    _status = _status with { IsQueued = true };
                }
            }

            if (shouldSignal)
            {
                _signal.Release();
            }
        }
    }
}

public sealed record LibraryScanStatus(
    bool IsInProgress,
    bool IsQueued,
    DateTime? StartedUtc,
    DateTime? LastCompletedUtc,
    string? LastError);
