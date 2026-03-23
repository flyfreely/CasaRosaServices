using System.Net.Http.Json;

namespace EmailChecker;

class WebhookSender(SubscriberRegistry registry)
{
    // HttpClient is thread-safe and intended to be reused for the lifetime of the app.
    private static readonly HttpClient _http = new();

    /// <summary>
    /// Posts a GuestMessagePending event to all registered subscribers concurrently.
    /// Per-subscriber failures are tracked; a subscriber is auto-removed after 3 consecutive failures.
    /// </summary>
    public async Task NotifyAsync()
    {
        var subscribers = registry.GetAll();
        if (subscribers.Count == 0)
        {
            Console.WriteLine("No subscribers registered, skipping webhook.");
            return;
        }

        await Task.WhenAll(subscribers.Select(PostAsync));
    }

    private async Task PostAsync(Subscriber subscriber)
    {
        try
        {
            var payload = new { @event = "GuestMessagePending", timestamp = DateTime.UtcNow };

            using var request = new HttpRequestMessage(HttpMethod.Post, subscriber.Url)
            {
                Content = JsonContent.Create(payload)
            };

            if (!string.IsNullOrEmpty(subscriber.Token))
                request.Headers.Add("X-Webhook-Token", subscriber.Token);

            var response = await _http.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                registry.RecordSuccess(subscriber.Url);
                Console.WriteLine($"Webhook → {subscriber.Url}: {(int)response.StatusCode}");
            }
            else
            {
                registry.RecordFailure(subscriber.Url);
                Console.WriteLine($"Webhook → {subscriber.Url}: {(int)response.StatusCode} (failure recorded)");
            }
        }
        catch (Exception ex)
        {
            registry.RecordFailure(subscriber.Url);
            Console.WriteLine($"Webhook failed for {subscriber.Url}: {ex.Message}");
        }
    }
}
