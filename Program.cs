using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using MailKit;
using MailKit.Net.Imap;
using MimeKit;

class Program
{
    private static readonly object lockObj = new object();
    private static List<DateTime> successfulPollTimestamps = new List<DateTime>();
    private static bool RemoteValue => CheckRemoteValue();

    // Signal for refresh request
    private static ManualResetEventSlim refreshSignal = new ManualResetEventSlim(false);
    // Signal for refresh completion
    private static ManualResetEventSlim refreshCompletedSignal = new ManualResetEventSlim(false);

    // Shared IMAP client and lock to protect access/reconnects
    private static ImapClient imapClient = CreateImapClient();
    private static readonly object imapLock = new object();

    // Credentials and server info extracted to variables for reuse
    private static readonly NetworkCredential imapCredentials = new NetworkCredential("casarosahouse@gmail.com", "dzpq xidy uosf qdmf");
    private static readonly Uri imapUri = new Uri("imaps://imap.gmail.com");

    static void Main(string[] args)
    {
        Thread apiThread = new Thread(StartApiServer) { IsBackground = true };
        apiThread.Start();

        Thread pollingThread = new Thread(StartPolling) { IsBackground = true };
        pollingThread.Start();

        Console.WriteLine("Press Enter to exit...");
        Console.ReadLine();

        // Clean up IMAP client on exit
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

    private static ImapClient CreateImapClient()
    {
        var client = new ImapClient();
        client.CheckCertificateRevocation = false;
        return client;
    }

    /// <summary>
    /// Ensure the shared IMAP client is connected and authenticated.
    /// Only reconnects if necessary. Thread-safe.
    /// Throws exceptions to caller so they can handle transient failures.
    /// </summary>
    public static void EnsureImapConnected()
    {
        lock (imapLock)
        {
            if (imapClient != null && imapClient.IsConnected && imapClient.IsAuthenticated)
            {
                // Already connected and authenticated
                return;
            }

            // If prior client exists but is connected, try to disconnect cleanly first
            try
            {
                if (imapClient != null && imapClient.IsConnected)
                {
                    imapClient.Disconnect(true);
                }
            }
            catch
            {
                // swallow disconnect errors and recreate client below
            }

            // Ensure we have a fresh client
            try
            {
                if (imapClient == null)
                    imapClient = CreateImapClient();

                // Connect and authenticate
                imapClient.Connect(imapUri);
                // Remove XOAUTH2 if not using OAuth2
                imapClient.AuthenticationMechanisms.Remove("XOAUTH2");
                imapClient.Authenticate(imapCredentials);

                // Open inbox read-only
                imapClient.Inbox.Open(FolderAccess.ReadOnly);
            }
            catch
            {
                // If anything failed, dispose current client and replace with a fresh instance for next attempt
                try
                {
                    imapClient.Dispose();
                }
                catch { }
                imapClient = CreateImapClient();
                throw;
            }
        }
    }

    private static bool CheckRemoteValue()
    {
        lock (lockObj)
        {
            DateTime cutoff = DateTime.UtcNow.AddMinutes(-10);
            successfulPollTimestamps.RemoveAll(t => t < cutoff);
            return successfulPollTimestamps.Count > 0;
        }
    }

    private static void StartApiServer()
    {
        var listener = new HttpListener();
        listener.Prefixes.Add("http://localhost:5000/");

        try
        {
            listener.Start();
            Console.WriteLine("API Server started at http://localhost:5000/");

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

    private static void SendResponse(HttpListenerContext context, string code)
    {
        byte[] buffer = Encoding.UTF8.GetBytes(code);
        if (code.Length > 0)
        {
            context.Response.StatusCode = 200;
            context.Response.ContentLength64 = buffer.Length;
            context.Response.OutputStream.Write(buffer, 0, buffer.Length);
        }
        else
        {
            context.Response.StatusCode = 201;
        }
        context.Response.OutputStream.Close();
    }

    private static void HandleRequest(HttpListenerContext context)
    {
        string path = context.Request.Url.AbsolutePath;

        var query = context.Request.QueryString;
        string token = query["Token"];

        // Check token first, return 404 if missing or wrong
        if (token==null || !token.Equals("a812"))
        {
            context.Response.StatusCode = 404;
            context.Response.OutputStream.Close();
            return;
        }

        if (path.Equals("/GetStatus", StringComparison.OrdinalIgnoreCase))
        {
            bool remoteValue = RemoteValue;
            context.Response.StatusCode = remoteValue ? 200 : 201;
            string responseString = remoteValue ? "Remote value is true" : "Remote value is false";
            byte[] buffer = Encoding.UTF8.GetBytes(responseString);
            context.Response.ContentLength64 = buffer.Length;
            context.Response.OutputStream.Write(buffer, 0, buffer.Length);
            context.Response.OutputStream.Close();
        }
        else if (path.Equals("/AirbnbSMSPin", StringComparison.OrdinalIgnoreCase))
        {
            var airbnbCode = RetrieveAirbnbVerificationCode(true);
            SendResponse(context, airbnbCode);
        }
        else if (path.Equals("/AirbnbEmailPin", StringComparison.OrdinalIgnoreCase))
        {
            var airbnbCode = RetrieveAirbnbVerificationCode(false);
            SendResponse(context, airbnbCode);
        }

        else if (path.Equals("/Refresh", StringComparison.OrdinalIgnoreCase))
        {
            refreshCompletedSignal.Reset();
            refreshSignal.Set();

            // Wait for polling thread to complete refresh
            if (refreshCompletedSignal.Wait(TimeSpan.FromSeconds(30))) // timeout 30 sec
            {
                string responseString = "Refresh completed";
                byte[] buffer = Encoding.UTF8.GetBytes(responseString);
                context.Response.StatusCode = 200;
                context.Response.ContentLength64 = buffer.Length;
                context.Response.OutputStream.Write(buffer, 0, buffer.Length);
            }
            else
            {
                string responseString = "Refresh timeout";
                byte[] buffer = Encoding.UTF8.GetBytes(responseString);
                context.Response.StatusCode = 504; // Gateway Timeout
                context.Response.ContentLength64 = buffer.Length;
                context.Response.OutputStream.Write(buffer, 0, buffer.Length);
            }

            context.Response.OutputStream.Close();
            refreshSignal.Reset();
        }
        else
        {
            context.Response.StatusCode = 404;
            context.Response.OutputStream.Close();
        }
    }

    private static void StartPolling()
    {
        Console.WriteLine("Polling thread started.");

        while (true)
        {
            // Wait either for refresh signal or timeout of 5 minutes
            if (refreshSignal.Wait(TimeSpan.FromMinutes(5)))
            {
                // Refresh requested - call GetRemoteData immediately
                Console.WriteLine($"{DateTime.UtcNow}: Refresh signal received, polling immediately.");
            }
            else
            {
                Console.WriteLine($"{DateTime.UtcNow}: Regular polling interval.");
            }

            bool apiResult = GetRemoteData();

            if (apiResult)
            {
                lock (lockObj)
                {
                    successfulPollTimestamps.Add(DateTime.UtcNow);
                }
                Console.WriteLine($"{DateTime.UtcNow}: Poll successful, RemoteValue set to true.");
            }
            else
            {
                Console.WriteLine($"{DateTime.UtcNow}: Poll returned false.");
            }

            // Signal that refresh (if any) is done
            if (refreshSignal.IsSet)
            {
                refreshCompletedSignal.Set();
            }
        }
    }

    private static void RefreshInbox()
    {
        EnsureImapConnected();
        // Either:
        imapClient.NoOp();
        // Or:
        // imapClient.NoOp();
    }

    private static bool GetRemoteData()
    {
        // Replace with actual API call logic.
        // For demo, returns true randomly 50% of the time.
        return new Random().NextDouble() > 0.5;
    }

    // Connect method retained for compatibility but made to call EnsureImapConnected
    public static void Connect(ImapClient? client)
    {
        // Prefer using the shared client. If a client is provided, ensure it's connected.
        if (client != null)
        {
            try
            {
                client.CheckCertificateRevocation = false;
                if (!client.IsConnected || !client.IsAuthenticated)
                {
                    client.Connect(imapUri);
                    client.AuthenticationMechanisms.Remove("XOAUTH2");
                    client.Authenticate(imapCredentials);
                    client.Inbox.Open(FolderAccess.ReadOnly);
                }
            }
            catch
            {
                try { if (client.IsConnected) client.Disconnect(true); } catch { }
                throw;
            }
        }
        else
        {
            // Ensure shared client is connected
            EnsureImapConnected();
        }
    }

    public static string SMSPinCheck(MimeMessage? email)
    {
        if (email.From.ToString().Contains("voice"))
        {
            string sub = email.Body.ToString().Trim();
            string plainText = Regex.Replace(sub, "<[^>]+?>", " ");
            plainText = System.Net.WebUtility.HtmlDecode(plainText).Trim();
            string toMatch = "Airbnb verification code is ";
            var loc = plainText.IndexOf(toMatch);
            if (loc > 0)
            {
                var substr = plainText.Substring(loc + toMatch.Length, 6);
                string[] split = substr.Split('.');
                return split[0];
            }
        }
        return string.Empty;
    }

    public static string SecurityCodeCheck(MimeMessage? email)
    {
        string toMatch = "Your security code is ";
        string subject = email.Subject.ToString();
        if (subject.Contains(toMatch))
        {
            var substr = subject.Substring(toMatch.Length, 6);
            string[] split = substr.Split('.');
            return split[0];
        }
        return string.Empty;
    }

    public static string RetrieveAirbnbVerificationCode(bool IsSMSPin)
    {
        string airbnbCode = string.Empty;

        Console.WriteLine("Retreiving PIN");
        // Use the shared IMAP client. Ensure connection first; if there's an error, attempt one reconnect then fail gracefully.
        try
        {
            EnsureImapConnected();
        }
        catch (Exception ex)
        {
            Console.WriteLine("Failed to connect to IMAP server: " + ex.Message);
            return airbnbCode;
        }

        // Lock access to the IMAP client while enumerating messages to avoid concurrent state issues.
        lock (imapLock)
        {
            try
            {
                RefreshInbox();
                if (imapClient.Inbox.Count > 0)
                {
                    // check the last up to 5 emails
                    int end = imapClient.Inbox.Count - 5;
                    if (end < 0) end = 0;
                    for (int i = imapClient.Inbox.Count - 1; i >= end; i--)
                    {
                        var email = imapClient.Inbox.GetMessage(i);
                        Console.WriteLine($"Looking at email date {email.Date} from {email.From}");
                        if (email.Date > DateTime.Now.AddMinutes(-10))
                        {
                            if (IsSMSPin)
                            {
                                airbnbCode = SMSPinCheck(email);
                            }
                            else
                            {
                                airbnbCode = SecurityCodeCheck(email);
                            }

                            if (!string.IsNullOrEmpty(airbnbCode))
                                break;
                        }
                    }
                    Console.WriteLine($"Airbnb code found: {airbnbCode}");
                }
            }
            catch (Exception ex)
            {
                // If operation fails due to connection, attempt to reset connection once.
                Console.WriteLine("Error reading IMAP messages: " + ex.Message);
                try
                {
                    if (imapClient.IsConnected)
                        imapClient.Disconnect(true);
                }
                catch { }
                // Replace client to ensure a clean state next time
                try { imapClient.Dispose(); } catch { }
                imapClient = CreateImapClient();
            }
        }

        return airbnbCode;
    }
}
