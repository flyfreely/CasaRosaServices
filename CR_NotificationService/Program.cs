using System.Net.Http.Json;

var builder = WebApplication.CreateBuilder(args);
var app     = builder.Build();

var webhookToken = builder.Configuration["Webhook:Token"] ?? string.Empty;

app.MapPost("/notify", async (HttpContext ctx, GuestMessageEvent evt) =>
{
    if (ctx.Request.Headers["X-Webhook-Token"].ToString() != webhookToken)
        return Results.NotFound();

    Console.WriteLine($"[{DateTime.UtcNow:u}] Guest message event received: {evt.Event}");

    await HandleNotificationAsync(evt);

    return Results.Ok();
});

// Register with EmailChecker once the server is ready to receive webhooks.
app.Lifetime.ApplicationStarted.Register(() =>
    _ = RegisterWithEmailCheckerAsync(builder.Configuration));

app.Run();

// ---------------------------------------------------------------------------
// Add your notification logic here (SMS, push, smart home, Slack, etc.)
// ---------------------------------------------------------------------------
static async Task HandleNotificationAsync(GuestMessageEvent evt)
{
    // Example: await smsClient.SendAsync("Guest message pending!");
    await Task.CompletedTask;
}

// ---------------------------------------------------------------------------
// Self-registration with EmailChecker (retries with exponential back-off)
// ---------------------------------------------------------------------------
static async Task RegisterWithEmailCheckerAsync(IConfiguration config)
{
    string? baseUrl   = config["EmailChecker:BaseUrl"];
    string? apiToken  = config["EmailChecker:ApiToken"];
    string? selfUrl   = config["Webhook:SelfUrl"];
    string? selfToken = config["Webhook:Token"];

    if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(selfUrl))
    {
        Console.WriteLine("EmailChecker:BaseUrl or Webhook:SelfUrl not configured — skipping registration.");
        return;
    }

    using var http = new HttpClient();
    string    subscribeUrl = $"{baseUrl}/subscribe?Token={apiToken}";
    var       payload      = new { url = selfUrl, token = selfToken };

    for (int attempt = 1; attempt <= 5; attempt++)
    {
        try
        {
            var response = await http.PostAsJsonAsync(subscribeUrl, payload);
            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Registered with EmailChecker at {baseUrl}");
                return;
            }
            Console.WriteLine($"Registration attempt {attempt} failed: HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Registration attempt {attempt} failed: {ex.Message}");
        }

        // Exponential back-off: 2s, 4s, 8s, 16s
        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
    }

    Console.WriteLine("Could not register with EmailChecker after 5 attempts.");
}

record GuestMessageEvent(string Event, DateTime Timestamp);
