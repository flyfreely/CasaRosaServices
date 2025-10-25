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

    static void Main(string[] args)
    {
        Thread apiThread = new Thread(StartApiServer) { IsBackground = true };
        apiThread.Start();

        Thread pollingThread = new Thread(StartPolling) { IsBackground = true };
        pollingThread.Start();

        Console.WriteLine("Press Enter to exit...");
        Console.ReadLine();
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

    private static void HandleRequest(HttpListenerContext context)
    {
        string path = context.Request.Url.AbsolutePath;

        var query = context.Request.QueryString;
        string token = query["Token"];

        // Check token first, return 404 if missing or wrong
        if (token != "a812")
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

    private static bool GetRemoteData()
    {
        // Replace with actual API call logic.
        // For demo, returns true randomly 50% of the time.
        return new Random().NextDouble() > 0.5;
    }

    public static void Connect(ImapClient? client)
    {
        var credentials = new NetworkCredential("casarosahouse@gmail.com", "dzpq xidy uosf qdmf");
        var uri = new Uri("imaps://imap.gmail.com");
        client.CheckCertificateRevocation = false;
        client.Connect(uri);

        // Remove the XOAUTH2 authentication mechanism since we don't have an OAuth2 token.
        client.AuthenticationMechanisms.Remove("XOAUTH2");

        client.Authenticate(credentials);

        client.Inbox.Open(FolderAccess.ReadOnly);
    }

    public static string PinCheck(MimeMessage? email)
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

    public static string RetrieveAirbnbVerificationCode(bool IsSMSPin)
    {
        string airbnbCode = string.Empty;
        using (var client = new ImapClient())
        {
            Connect(client);
            // keep track of the messages
            IList<IMessageSummary> messages = null;
            int count = 0;

            if (client.Inbox.Count > 0)
            {
                // check the last 3 emails
                int end = client.Inbox.Count - 5;
                if (end < 0) end = 0;
                for (int i = client.Inbox.Count - 1; i >= end; i--)
                {
                    var email = client.Inbox.GetMessage(i);
                    Console.WriteLine($"Looking at email date {email.Date} from {email.From}");
                    if (email.Date > DateTime.Now.AddMinutes(-2))
                    {
                        if (IsSMSPin)
                        {
                            airbnbCode=PinCheck(email);

                        }
                        else
                        {
                            string toMatch = "Your security code is ";
                            string subject = email.Subject.ToString();
                            if (subject.Contains(toMatch))
                            {
                                var substr = subject.Substring(toMatch.Length, 6);
                                string[] split = substr.Split('.');
                                airbnbCode = split[0];
                                break;
                            }
                        }
                    }
                    ;
                }
                Console.WriteLine($"Airbnb code sent {airbnbCode}");
            }
            client.Disconnect(true);
        }
        return airbnbCode;
    }
}
