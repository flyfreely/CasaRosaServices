using EmailChecker;
using MailKit;
using MailKit.Net.Imap;
using Microsoft.Extensions.Configuration;
using MimeKit;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

class Program
{
    // Configurable values loaded from appsettings.json
    private static IConfiguration config;

    // General configuration values
    private static int resetToDefaultMinutes;
    private static int pollingIntervalMinutes;
    private static int refreshTimeoutSeconds;
    private static int lookbackMinutes;
    private static int maxMessagesToScan;
    private static string apiToken;

    // IMAP info
    private static string imapScheme;
    private static string imapHost;
    private static int imapPort;
    private static bool checkCrl;
    private static string imapUser;
    private static string imapPass;
    private static bool useOAuth2;

    // HTTP listener prefixes
    private static string[] httpPrefixes;

    private static readonly object lockObj = new object();
    private static bool airbnbMessagesPending = false;

    private static ImapClient imapClient;
    private static readonly object imapLock = new object();
    private static NetworkCredential imapCredentials;
    private static Uri imapUri;

    private static DateTime lastPollTime = DateTime.MinValue;
    private static DateTime lastImportantMessageAlarm = DateTime.MinValue;

    private static ManualResetEventSlim refreshSignal = new ManualResetEventSlim(false);
    private static ManualResetEventSlim refreshCompletedSignal = new ManualResetEventSlim(false);

    public enum TypeOfMessageCheck
    {
        SMSPin,
        EmailCode,
        AirbnbCurrentGuestMessage
    }

    static void Main(string[] args)
    {
        LoadConfiguration();

        imapCredentials = new NetworkCredential(imapUser, imapPass);
        imapUri = new Uri($"{imapScheme}://{imapHost}:{imapPort}");
        imapClient = CreateImapClient();

        Thread apiThread = new Thread(StartApiServer) { IsBackground = true };
        apiThread.Start();

        Thread pollingThread = new Thread(StartPolling) { IsBackground = true };
        pollingThread.Start();

        Console.WriteLine("Press Enter to exit...");
        Console.ReadLine();

        lock (imapLock)
        {
            try
            {
                if (imapClient.IsConnected)
                    imapClient.Disconnect(true);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error disconnecting IMAP client on exit: " + ex.Message);
            }
            finally
            {
                imapClient.Dispose();
            }
        }
    }

    private static void LoadConfiguration()
    {
        config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddEnvironmentVariables()
            .Build();

        // HTTP and token
        httpPrefixes = config.GetSection("Http:Prefixes").Get<string[]>();
        apiToken = config["Http:AuthToken"];

        // IMAP
        imapScheme = config["Imap:Scheme"];
        imapHost = config["Imap:Host"];
        imapPort = int.Parse(config["Imap:Port"] ?? "993");
        checkCrl = bool.Parse(config["Imap:CheckCertificateRevocation"] ?? "false");
        imapUser = config["Imap:Username"];
        imapPass = config["Imap:Password"];
        useOAuth2 = bool.Parse(config["Imap:UseOAuth2"] ?? "false");

        // Airbnb / Polling
        resetToDefaultMinutes = int.Parse(config["Airbnb:ResetToDefaultMinutes"] ?? "10");
        pollingIntervalMinutes = int.Parse(config["Polling:DefaultIntervalMinutes"] ?? "60");
        refreshTimeoutSeconds = int.Parse(config["Polling:ImmediateRefreshTimeoutSeconds"] ?? "30");
        lookbackMinutes = int.Parse(config["Polling:LookbackMinutes"] ?? "10");
        maxMessagesToScan = int.Parse(config["Polling:MaxMessagesToScan"] ?? "5");
    }

    private static ImapClient CreateImapClient()
    {
        var client = new ImapClient();
        client.CheckCertificateRevocation = checkCrl;
        return client;
    }

    public static void EnsureImapConnected()
    {
        lock (imapLock)
        {
            if (imapClient != null && imapClient.IsConnected && imapClient.IsAuthenticated)
                return;

            try
            {
                if (imapClient != null && imapClient.IsConnected)
                    imapClient.Disconnect(true);
            }
            catch { }

            try
            {
                if (imapClient == null)
                    imapClient = CreateImapClient();

                imapClient.Connect(imapUri);
                if (!useOAuth2)
                    imapClient.AuthenticationMechanisms.Remove("XOAUTH2");

                imapClient.Authenticate(imapCredentials);
                imapClient.Inbox.Open(FolderAccess.ReadOnly);
            }
            catch
            {
                try { imapClient.Dispose(); } catch { }
                imapClient = CreateImapClient();
                throw;
            }
        }
    }

    private static void StartApiServer()
    {
        var listener = new HttpListener();
        foreach (var prefix in httpPrefixes)
            listener.Prefixes.Add(prefix);

        try
        {
            listener.Start();
            Console.WriteLine($"API Server started at: {string.Join(", ", httpPrefixes)}");

            while (true)
            {
                var context = listener.GetContext();
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        HandleRequest(context);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Error handling request: " + ex.Message);
                    }
                });
            }
        }
        catch (HttpListenerException hlex)
        {
            Console.WriteLine("HTTP Listener error: " + hlex.Message);
        }
        finally
        {
            listener.Close();
        }
    }

    private static void HandleRequest(HttpListenerContext context)
    {
        string path = context.Request.Url.AbsolutePath;
        string token = context.Request.QueryString["Token"];

        if (token == null || token != apiToken)
        {
            context.Response.StatusCode = 404;
            context.Response.OutputStream.Close();
            return;
        }

        Console.WriteLine($"Request received {path}");

        if (path.Equals("/GetStatus", StringComparison.OrdinalIgnoreCase))
        {
            bool remoteValue = airbnbMessagesPending;
            context.Response.StatusCode = remoteValue ? 200 : 201;
            WriteResponse(context, remoteValue ? "Remote value is true" : "Remote value is false");
        }
        else if (path.Equals("/AirbnbSMSPin", StringComparison.OrdinalIgnoreCase))
        {
            var airbnbCode = RetrieveAirbnbVerificationCode(TypeOfMessageCheck.SMSPin);
            WriteResponse(context, airbnbCode);
        }
        else if (path.Equals("/AirbnbEmailPin", StringComparison.OrdinalIgnoreCase))
        {
            var airbnbCode = RetrieveAirbnbVerificationCode(TypeOfMessageCheck.EmailCode);
            WriteResponse(context, airbnbCode);
        }
        else if (path.Equals("/AirbnbMessages", StringComparison.OrdinalIgnoreCase))
        {
            HandleAirbnbMessages(context);
        }
        else
        {
            context.Response.StatusCode = 404;
            context.Response.OutputStream.Close();
        }
    }

    private static void WriteResponse(HttpListenerContext context, string content)
    {
        byte[] buffer = Encoding.UTF8.GetBytes(content ?? "");
        context.Response.StatusCode = 200;
        context.Response.ContentLength64 = buffer.Length;
        context.Response.OutputStream.Write(buffer, 0, buffer.Length);
        context.Response.OutputStream.Close();
    }

    private static void HandleAirbnbMessages(HttpListenerContext context)
    {
        lastPollTime = DateTime.Now;

        if (pollingIntervalMinutes > 1)
        {
            refreshCompletedSignal.Reset();
            refreshSignal.Set();
            pollingIntervalMinutes = 1;

            if (refreshCompletedSignal.Wait(TimeSpan.FromSeconds(refreshTimeoutSeconds)))
            {
                context.Response.StatusCode = (airbnbMessagesPending &&
                    lastImportantMessageAlarm.AddMinutes(resetToDefaultMinutes) < DateTime.Now)
                    ? 200 : 201;

                lastImportantMessageAlarm = DateTime.Now;
                Console.WriteLine($"Returning {context.Response.StatusCode} from poll");
            }
            else
            {
                context.Response.StatusCode = 504;
                WriteResponse(context, "Refresh timeout");
                Console.WriteLine("Timeout polling");
                return;
            }
        }
        else
        {
            if (airbnbMessagesPending &&
                lastImportantMessageAlarm.AddMinutes(resetToDefaultMinutes) < DateTime.Now)
            {
                lastImportantMessageAlarm = DateTime.Now;
                context.Response.StatusCode = 200;
            }
            else
            {
                context.Response.StatusCode = 201;
            }

            Console.WriteLine($"Returning {context.Response.StatusCode} from cache");
        }

        WriteResponse(context, airbnbMessagesPending ? "Remote value is true" : "Remote value is false");
    }

    private static void StartPolling()
    {
        Console.WriteLine("Polling thread started.");

        while (true)
        {
            bool refreshTriggered = refreshSignal.Wait(TimeSpan.FromMinutes(pollingIntervalMinutes));

            if (refreshTriggered)
            {
                Console.WriteLine($"{DateTime.UtcNow}: Refresh signal received, polling immediately.");
                refreshSignal.Reset();
            }
            else
            {
                Console.WriteLine($"{DateTime.UtcNow}: Regular polling interval.");
            }

            if (lastPollTime.AddMinutes(resetToDefaultMinutes) < DateTime.Now)
                pollingIntervalMinutes = int.Parse(config["Polling:DefaultIntervalMinutes"] ?? "60");

            var airbnbCode = RetrieveAirbnbVerificationCode(TypeOfMessageCheck.AirbnbCurrentGuestMessage);

            lock (lockObj)
                airbnbMessagesPending = !string.IsNullOrEmpty(airbnbCode);

            Console.WriteLine($"{DateTime.UtcNow}: Poll successful, {airbnbMessagesPending}.");

            if (refreshTriggered)
                refreshCompletedSignal.Set();
        }
    }

    private static void RefreshInbox()
    {
        EnsureImapConnected();
        imapClient.NoOp();
    }

    public static string SMSPinCheck(MimeMessage? email)
    {
        if (email.From.ToString().Contains("voice"))
        {
            string sub = email.Body.ToString().Trim();
            string plainText = Regex.Replace(sub, "<[^>]+?>", " ");
            plainText = WebUtility.HtmlDecode(plainText).Trim();
            string toMatch = "Airbnb verification code is ";
            int loc = plainText.IndexOf(toMatch);
            if (loc > 0)
            {
                var substr = plainText.Substring(loc + toMatch.Length, 6);
                string[] split = substr.Split('.');
                return split[0];
            }
        }
        return string.Empty;
    }

    public static bool CurrentGuestMessageFromAirbnb(MimeMessage? email)
    {
        DateTime now = DateTime.Now;
        string toMatch = "Reservation for ";
        string subject = email.Subject ?? "";
        if (subject.Contains(toMatch))
        {
            bool currentGuest = AirbnbDateParser.TryParseDateRange(subject, out DateTime checkIn, out DateTime checkOut, now);
            return currentGuest && now >= checkIn && now < checkOut;
        }
        return false;
    }

    public static string EmailCodeCheck(MimeMessage? email)
    {
        string toMatch = "Your security code is ";
        string subject = email.Subject ?? "";
        if (subject.Contains(toMatch))
        {
            var substr = subject.Substring(toMatch.Length, 6);
            string[] split = substr.Split('.');
            return split[0];
        }
        return string.Empty;
    }

    public static string RetrieveAirbnbVerificationCode(TypeOfMessageCheck messageType)
    {
        string airbnbCode = string.Empty;

        Console.WriteLine("Retrieving PIN");
        try
        {
            EnsureImapConnected();
        }
        catch (Exception ex)
        {
            Console.WriteLine("Failed to connect to IMAP server: " + ex.Message);
            return airbnbCode;
        }

        lock (imapLock)
        {
            try
            {
                RefreshInbox();
                if (imapClient.Inbox.Count > 0)
                {
                    int end = Math.Max(0, imapClient.Inbox.Count - maxMessagesToScan);
                    for (int i = imapClient.Inbox.Count - 1; i >= end; i--)
                    {
                        var email = imapClient.Inbox.GetMessage(i);
                        Console.WriteLine($"Checking email date {email.Date} from {email.From}");

                        if (email.Date > DateTime.Now.AddMinutes(-lookbackMinutes))
                        {
                            if (messageType == TypeOfMessageCheck.SMSPin)
                                airbnbCode = SMSPinCheck(email);
                            else if (messageType == TypeOfMessageCheck.EmailCode)
                                airbnbCode = EmailCodeCheck(email);
                            else if (messageType == TypeOfMessageCheck.AirbnbCurrentGuestMessage)
                                if (CurrentGuestMessageFromAirbnb(email))
                                    airbnbCode = "CurrentGuestMessageFound";

                            if (!string.IsNullOrEmpty(airbnbCode))
                                break;
                        }
                    }
                    Console.WriteLine($"Airbnb code found: {airbnbCode}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error reading IMAP messages: " + ex.Message);
                try { if (imapClient.IsConnected) imapClient.Disconnect(true); } catch { }
                try { imapClient.Dispose(); } catch { }
                imapClient = CreateImapClient();
            }
        }

        return airbnbCode;
    }
}
