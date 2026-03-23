using Microsoft.Extensions.Configuration;

namespace EmailChecker;

class PollingService(AppConfig config, IConfiguration liveConfig, ImapService imap, WebhookSender webhook)
{
    private int      _intervalMinutes = config.DefaultPollingIntervalMinutes;
    private DateTime _lastPollTime    = DateTime.MinValue;

    private bool     _messagesPending;
    private readonly object _stateLock = new();

    private readonly ManualResetEventSlim _refreshSignal    = new(initialState: false);
    private readonly ManualResetEventSlim _refreshCompleted = new(initialState: false);

    public bool MessagesPending
    {
        get { lock (_stateLock) return _messagesPending; }
    }

    /// <summary>
    /// Records that the caller is actively monitoring, keeping fast-poll mode alive.
    /// </summary>
    public void RecordActivity() => _lastPollTime = DateTime.Now;

    /// <summary>
    /// Switches to fast-poll mode (1 min interval), triggers an immediate refresh,
    /// and waits for it to complete. Returns false on timeout.
    /// </summary>
    public bool RequestRefreshAndWait(TimeSpan timeout, out bool pending)
    {
        _refreshCompleted.Reset();
        _refreshSignal.Set();
        _intervalMinutes = 1;

        bool completed = _refreshCompleted.Wait(timeout);
        pending = MessagesPending;
        return completed;
    }

    public bool IsInFastMode => _intervalMinutes <= 1;

    /// <summary>Runs the polling loop indefinitely. Intended to run on a background thread.</summary>
    public void Run()
    {
        Console.WriteLine("Polling thread started.");

        while (true)
        {
            bool signaled = _refreshSignal.Wait(TimeSpan.FromMinutes(_intervalMinutes));

            if (signaled)
            {
                Console.WriteLine($"{DateTime.UtcNow}: Refresh signal received, polling immediately.");
                _refreshSignal.Reset();
            }
            else
            {
                Console.WriteLine($"{DateTime.UtcNow}: Regular polling interval.");
            }

            ResetIntervalIfIdle();

            string result     = imap.Scan(MessageCheckType.CurrentGuestMessage);
            bool   nowPending = !string.IsNullOrEmpty(result);

            bool wasPending;
            lock (_stateLock)
            {
                wasPending       = _messagesPending;
                _messagesPending = nowPending;
            }

            Console.WriteLine($"{DateTime.UtcNow}: Poll done, pending={_messagesPending}.");

            // Fire webhook only on the false→true edge so we don't spam on every poll.
            if (nowPending && !wasPending)
                _ = webhook.NotifyAsync();

            if (signaled)
                _refreshCompleted.Set();
        }
    }

    private void ResetIntervalIfIdle()
    {
        if (_lastPollTime.AddMinutes(config.ResetToDefaultMinutes) < DateTime.Now)
            _intervalMinutes = int.Parse(liveConfig["Polling:DefaultIntervalMinutes"] ?? "60");
    }
}
