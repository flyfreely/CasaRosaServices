using MailKit;
using MailKit.Net.Imap;
using MimeKit;
using System.Net;
using System.Text.RegularExpressions;

namespace EmailChecker;

enum MessageCheckType { SmsPin, EmailCode, CurrentGuestMessage }

class ImapService(AppConfig config)
{
    private readonly NetworkCredential _credentials = new(config.ImapUsername, config.ImapPassword);
    private readonly Uri               _serverUri   = new($"{config.ImapScheme}://{config.ImapHost}:{config.ImapPort}");
    private readonly object            _lock        = new();
    private ImapClient _client = CreateClient(config);

    /// <summary>
    /// Refreshes the inbox and scans recent messages for the specified check type.
    /// Returns the matched value, or empty string if nothing found.
    /// </summary>
    public string Scan(MessageCheckType type)
    {
        Console.WriteLine("Scanning inbox...");

        try
        {
            EnsureConnected();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to connect to IMAP server: {ex.Message}");
            return string.Empty;
        }

        lock (_lock)
        {
            try
            {
                Refresh();
                return ScanRecentMessages(type);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading IMAP messages: {ex.Message}");
                ResetClient();
                return string.Empty;
            }
        }
    }

    public void Disconnect()
    {
        lock (_lock)
        {
            try { if (_client.IsConnected) _client.Disconnect(true); } catch { }
            _client.Dispose();
        }
    }

    // --- Connection management ---

    private void EnsureConnected()
    {
        lock (_lock)
        {
            if (_client.IsConnected && _client.IsAuthenticated)
                return;

            TryDisconnect();
            Connect();
        }
    }

    private void Connect()
    {
        try
        {
            _client.Connect(_serverUri);

            if (!config.UseOAuth2)
                _client.AuthenticationMechanisms.Remove("XOAUTH2");

            _client.Authenticate(_credentials);
            _client.Inbox.Open(FolderAccess.ReadOnly);
        }
        catch
        {
            ResetClient();
            throw;
        }
    }

    private void Refresh()
    {
        EnsureConnected();
        _client.NoOp();
    }

    private void TryDisconnect()
    {
        try { if (_client.IsConnected) _client.Disconnect(true); } catch { }
    }

    private void ResetClient()
    {
        try { _client.Dispose(); } catch { }
        _client = CreateClient(config);
    }

    private static ImapClient CreateClient(AppConfig cfg) =>
        new() { CheckCertificateRevocation = cfg.CheckCertificateRevocation };

    // --- Message scanning ---

    private string ScanRecentMessages(MessageCheckType type)
    {
        if (_client.Inbox.Count == 0)
            return string.Empty;

        int    startIndex = Math.Max(0, _client.Inbox.Count - config.MaxMessagesToScan);
        var    cutoff     = DateTime.Now.AddMinutes(-config.LookbackMinutes);

        for (int i = _client.Inbox.Count - 1; i >= startIndex; i--)
        {
            var email = _client.Inbox.GetMessage(i);
            Console.WriteLine($"Checking email {email.Date} from {email.From}");

            if (email.Date <= cutoff)
                continue;

            string result = type switch
            {
                MessageCheckType.SmsPin               => CheckSmsPin(email),
                MessageCheckType.EmailCode             => CheckEmailCode(email),
                MessageCheckType.CurrentGuestMessage   => IsCurrentGuestMessage(email) ? "CurrentGuestMessageFound" : string.Empty,
                _                                      => string.Empty
            };

            if (!string.IsNullOrEmpty(result))
            {
                Console.WriteLine($"Match found: {result}");
                return result;
            }
        }

        return string.Empty;
    }

    // --- Email parsers ---

    private static string CheckSmsPin(MimeMessage email)
    {
        if (!email.From.ToString().Contains("voice"))
            return string.Empty;

        string body      = email.Body.ToString()!.Trim();
        string plainText = WebUtility.HtmlDecode(Regex.Replace(body, "<[^>]+?>", " ")).Trim();

        const string marker = "Airbnb verification code is ";
        int idx = plainText.IndexOf(marker);

        return idx >= 0
            ? plainText.Substring(idx + marker.Length, 6).Split('.')[0]
            : string.Empty;
    }

    private static string CheckEmailCode(MimeMessage email)
    {
        const string marker  = "Your security code is ";
        string       subject = email.Subject ?? string.Empty;

        return subject.Contains(marker)
            ? subject.Substring(marker.Length, 6).Split('.')[0]
            : string.Empty;
    }

    private static bool IsCurrentGuestMessage(MimeMessage email)
    {
        const string marker  = "Reservation for ";
        string       subject = email.Subject ?? string.Empty;

        if (!subject.Contains(marker))
            return false;

        Console.WriteLine($"Found potential current guest message: {subject}");

        return AirbnbDateParser.TryParseDateRange(subject, out DateTime checkIn, out DateTime checkOut, DateTime.Now)
            && DateTime.Now >= checkIn
            && DateTime.Now <= checkOut;
    }
}
