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

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var result = await httpClient.GetAsync(endpoint.Destination, cts.Token);
            var content = await result.Content.ReadAsStringAsync(cts.Token);

            _logger.LogInformation("Http: {endpoint.Destination} {result.StatusCode}",
                endpoint.Destination, result.StatusCode);

            if (endpoint.Keyword != "" && !content.Contains(endpoint.Keyword)) throw new ArgumentNullException(nameof(endpoint), "Keyword not found.");
            
            if (result.IsSuccessStatusCode)
            {
                await _pushService.PushAsync(endpoint.PushUri, stopwatch.ElapsedMilliseconds);
            }
        }
        catch
        {
            _logger.LogError("Error trying get {endpoint.Destination}", endpoint.Destination);
            return;
        }
    }
}