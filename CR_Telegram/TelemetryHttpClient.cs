using Microsoft.Extensions.Configuration;
using Serilog.Sinks.Http;

/// <summary>
/// Custom HTTP client for Serilog's HTTP sink that injects the TelemetryAPI key header.
/// </summary>
sealed class TelemetryHttpClient : IHttpClient
{
    static readonly HttpClient _http = new();

    public void Configure(IConfiguration configuration) { }

    public async Task<HttpResponseMessage> PostAsync(string requestUri, Stream contentStream,
        CancellationToken cancellationToken)
    {
        var content = new StreamContent(contentStream);
        content.Headers.Add("Content-Type", "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri) { Content = content };
        request.Headers.Add("X-Api-Key", "TelemetryLog387&@!");
        return await _http.SendAsync(request, cancellationToken);
    }

    public void Dispose() { }
}
