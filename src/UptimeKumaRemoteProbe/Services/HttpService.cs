namespace UptimeKumaRemoteProbe.Services;

public class HttpService
{
    private readonly ILogger<HttpService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PushService _pushService;

    public HttpService(ILogger<HttpService> logger, IHttpClientFactory httpClientFactory, PushService pushService)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _pushService = pushService;
    }

    public async Task CheckHttpAsync(Endpoint endpoint)
    {
        var httpClient = _httpClientFactory.CreateClient(endpoint.IgnoreSSL ? "IgnoreSSL" : "Default");
        var stopwatch = Stopwatch.StartNew();

        var destinations = endpoint.Destinations;

        bool anySuccess = false;

        foreach (var destination in destinations)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                using var result = await httpClient.GetAsync(destination, cts.Token);
                var content = await result.Content.ReadAsStringAsync(cts.Token);

                _logger.LogInformation("Http: {destination} {statusCode}", destination, result.StatusCode);

                if (result.IsSuccessStatusCode)
                {
                    if (endpoint.Keyword != "" && !content.Contains(endpoint.Keyword))
                    {
                        _logger.LogWarning("Keyword '{keyword}' not found in content for {destination}", endpoint.Keyword, destination);
                        continue;
                    }

                    anySuccess = true;
                    break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error trying to get {destination}", destination);
            }
        }

        if (anySuccess)
        {
            await _pushService.PushAsync(endpoint.PushUri, stopwatch.ElapsedMilliseconds);
        }
    }
}