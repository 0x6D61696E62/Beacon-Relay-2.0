namespace BeaconRelay.LpdReceiver.Services;

/// <summary>
/// Thread-safe singleton tracking whether delivery dispatching is paused.
/// Pause state is authoritative in memory; the DB record is for persistence across restarts.
/// </summary>
public sealed class DeliveryPauseState
{
    private readonly object _sync = new();
    private bool _isPaused;
    private DateTime? _resumeAtUtc;
    private string? _reason;
    private DateTime? _pausedAtUtc;

    public bool IsPaused
    {
        get
        {
            lock (_sync)
            {
                if (!_isPaused)
                {
                    return false;
                }

                if (_resumeAtUtc.HasValue && DateTime.UtcNow >= _resumeAtUtc.Value)
                {
                    _isPaused = false;
                    _resumeAtUtc = null;
                    _reason = null;
                    _pausedAtUtc = null;
                    return false;
                }

                return true;
            }
        }
    }

    public DateTime? ResumeAtUtc
    {
        get
        {
            lock (_sync)
            {
                return _resumeAtUtc;
            }
        }
    }

    public string? Reason
    {
        get
        {
            lock (_sync)
            {
                return _reason;
            }
        }
    }

    public DateTime? PausedAtUtc
    {
        get
        {
            lock (_sync)
            {
                return _pausedAtUtc;
            }
        }
    }

    public DeliveryPauseSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                var expired = _isPaused && _resumeAtUtc.HasValue && DateTime.UtcNow >= _resumeAtUtc.Value;
                if (expired)
                {
                    _isPaused = false;
                    _resumeAtUtc = null;
                    _reason = null;
                    _pausedAtUtc = null;
                }

                return new DeliveryPauseSnapshot(_isPaused, _pausedAtUtc, _resumeAtUtc, _reason);
            }
        }
    }

    public void Pause(DateTime? resumeAtUtc, string? reason)
    {
        lock (_sync)
        {
            _isPaused = true;
            _pausedAtUtc = DateTime.UtcNow;
            _resumeAtUtc = resumeAtUtc;
            _reason = reason;
        }
    }

    public void Resume()
    {
        lock (_sync)
        {
            _isPaused = false;
            _resumeAtUtc = null;
            _reason = null;
            _pausedAtUtc = null;
        }
    }
}

public sealed record DeliveryPauseSnapshot(
    bool IsPaused,
    DateTime? PausedAtUtc,
    DateTime? ResumeAtUtc,
    string? Reason);
