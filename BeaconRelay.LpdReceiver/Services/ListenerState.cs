namespace BeaconRelay.LpdReceiver.Services;

public sealed class ListenerState
{
    private readonly object _sync = new();
    private bool _isListening;
    private string? _lastError;
    private DateTime? _lastErrorUtc;

    public bool IsListening
    {
        get
        {
            lock (_sync)
            {
                return _isListening;
            }
        }
    }

    public string? LastError
    {
        get
        {
            lock (_sync)
            {
                return _lastError;
            }
        }
    }

    public DateTime? LastErrorUtc
    {
        get
        {
            lock (_sync)
            {
                return _lastErrorUtc;
            }
        }
    }

    public void MarkListening(bool isListening)
    {
        lock (_sync)
        {
            _isListening = isListening;
        }
    }

    public void MarkError(Exception ex)
    {
        lock (_sync)
        {
            _lastError = ex.Message;
            _lastErrorUtc = DateTime.UtcNow;
            _isListening = false;
        }
    }
}
