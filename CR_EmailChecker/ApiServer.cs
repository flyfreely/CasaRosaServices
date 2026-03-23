using System.Net;
using System.Text;
using System.Text.Json;

namespace EmailChecker;

class ApiServer(AppConfig config, PollingService poller, ImapService imap, SubscriberRegistry registry)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    // Rate-limits how often /AirbnbMessages returns 200 (one alarm per reset window).
    private DateTime _lastAlarmTime = DateTime.MinValue;

    /// <summary>Starts the HTTP listener and blocks, serving requests indefinitely.</summary>
    public void Run()
    {
        var listener = new HttpListener();
        foreach (var prefix in config.HttpPrefixes)
        {
            Console.WriteLine($"Listening: {prefix}");
            listener.Prefixes.Add(prefix);
        }

        try
        {
            listener.Start();
            Console.WriteLine($"API server started: {string.Join(", ", config.HttpPrefixes)}");
            AcceptLoop(listener);
        }
        catch (HttpListenerException ex)
        {
            Console.WriteLine($"HTTP listener error: {ex.Message}");
        }
        finally
        {
            listener.Close();
        }
    }

    private void AcceptLoop(HttpListener listener)
    {
        while (true)
        {
            var context = listener.GetContext();
            Task.Run(async () =>
            {
                try   { await HandleRequestAsync(context); }
                catch (Exception ex) { Console.WriteLine($"Error handling request: {ex.Message}"); }
            });
        }
    }

    // --- Request routing ---

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        string path   = context.Request.Url!.AbsolutePath;
        string method = context.Request.HttpMethod;
        string? token = context.Request.QueryString["Token"];

        if (token != config.ApiToken)
        {
            context.Response.StatusCode = 404;
            context.Response.OutputStream.Close();
            return;
        }

        Console.WriteLine($"Request: {method} {path}");

        if      (path.Equals("/GetStatus",      StringComparison.OrdinalIgnoreCase)) HandleGetStatus(context);
        else if (path.Equals("/AirbnbSMSPin",   StringComparison.OrdinalIgnoreCase)) WriteResponse(context, imap.Scan(MessageCheckType.SmsPin));
        else if (path.Equals("/AirbnbEmailPin", StringComparison.OrdinalIgnoreCase)) WriteResponse(context, imap.Scan(MessageCheckType.EmailCode));
        else if (path.Equals("/AirbnbMessages", StringComparison.OrdinalIgnoreCase)) HandleAirbnbMessages(context);
        else if (path.Equals("/subscribe",      StringComparison.OrdinalIgnoreCase) && method == "POST")   await HandleSubscribeAsync(context);
        else if (path.Equals("/subscribe",      StringComparison.OrdinalIgnoreCase) && method == "DELETE") HandleUnsubscribe(context);
        else
        {
            context.Response.StatusCode = 204;
            context.Response.OutputStream.Close();
        }
    }

    // --- Endpoint handlers ---

    private void HandleGetStatus(HttpListenerContext context)
    {
        bool pending = poller.MessagesPending;
        context.Response.StatusCode = pending ? 200 : 204;
        WriteResponse(context, pending ? "Remote value is true" : "Remote value is false");
    }

    private void HandleAirbnbMessages(HttpListenerContext context)
    {
        poller.RecordActivity();

        if (!poller.IsInFastMode)
        {
            // Trigger an immediate IMAP refresh and wait for it to complete.
            bool completed = poller.RequestRefreshAndWait(
                TimeSpan.FromSeconds(config.ImmediateRefreshTimeoutSeconds),
                out bool pending);

            if (!completed)
            {
                context.Response.StatusCode = 504;
                WriteResponse(context, "Refresh timeout");
                Console.WriteLine("Timeout polling");
                return;
            }

            // In the triggered-refresh path the alarm timer always resets,
            // so each external poll cycle gets at most one 200 per reset window.
            bool alarm = pending && _lastAlarmTime.AddMinutes(config.ResetToDefaultMinutes) < DateTime.Now;
            _lastAlarmTime = DateTime.Now;
            context.Response.StatusCode = alarm ? 200 : 204;
            Console.WriteLine($"Returning {context.Response.StatusCode} from poll");
        }
        else
        {
            // Already in fast mode — return the cached result immediately.
            bool pending = poller.MessagesPending;
            bool alarm   = pending && _lastAlarmTime.AddMinutes(config.ResetToDefaultMinutes) < DateTime.Now;
            if (alarm) _lastAlarmTime = DateTime.Now;
            context.Response.StatusCode = alarm ? 200 : 204;
            Console.WriteLine($"Returning {context.Response.StatusCode} from cache");
        }

        WriteResponse(context, poller.MessagesPending ? "Remote value is true" : "Remote value is false");
    }

    private async Task HandleSubscribeAsync(HttpListenerContext context)
    {
        using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
        string body = await reader.ReadToEndAsync();

        SubscribeRequest? req;
        try { req = JsonSerializer.Deserialize<SubscribeRequest>(body, JsonOptions); }
        catch { req = null; }

        if (string.IsNullOrWhiteSpace(req?.Url))
        {
            context.Response.StatusCode = 400;
            WriteResponse(context, "Missing or invalid 'url' field.");
            return;
        }

        registry.Add(new Subscriber(req.Url, req.Token));
        context.Response.StatusCode = 200;
        context.Response.OutputStream.Close();
    }

    private void HandleUnsubscribe(HttpListenerContext context)
    {
        string? url = context.Request.QueryString["url"];

        if (string.IsNullOrWhiteSpace(url))
        {
            context.Response.StatusCode = 400;
            WriteResponse(context, "Missing 'url' query parameter.");
            return;
        }

        registry.Remove(url);
        context.Response.StatusCode = 200;
        context.Response.OutputStream.Close();
    }

    // --- Helpers ---

    private static void WriteResponse(HttpListenerContext context, string content)
    {
        byte[] buffer = Encoding.UTF8.GetBytes(content ?? string.Empty);
        context.Response.ContentLength64 = buffer.Length;
        context.Response.OutputStream.Write(buffer, 0, buffer.Length);
        context.Response.OutputStream.Close();
    }
}

record SubscribeRequest(string? Url, string? Token);
