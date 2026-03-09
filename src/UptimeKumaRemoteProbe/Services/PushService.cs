namespace UptimeKumaRemoteProbe.Services;

public class PushService
{
    private readonly ILogger<PushService> _logger;
    private readonly HttpClient _httpClient;
    private readonly IHttpClientFactory _httpClientFactory;
    public PushService(ILogger<PushService> logger, HttpClient httpClient, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClient = httpClient;
        _httpClientFactory = httpClientFactory;
        _httpClient = _httpClientFactory.CreateClient();
    }

    public async Task PushAsync(Uri uri, long elapsedMilliseconds)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await _httpClient.GetAsync($"{uri}{elapsedMilliseconds}", cts.Token);
            _logger.LogDebug("Push: {uri} ({ms}ms)", uri, elapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            _logger.LogError("Push timed out after 15s: {uri}", uri);
        }
        catch
        {
            _logger.LogError("Error trying to push results to {uri}", uri);
        }
    }
}
