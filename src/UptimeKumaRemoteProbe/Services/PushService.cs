namespace UptimeKumaRemoteProbe.Services;

public class PushService
{
    private readonly ILogger<PushService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    
    public PushService(ILogger<PushService> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    public async Task PushAsync(Uri uri, long? elapsedMilliseconds = null)
    {
        try
        {
            var httpClient = _httpClientFactory.CreateClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var finalUri = elapsedMilliseconds.HasValue ? $"{uri}{elapsedMilliseconds}" : uri.ToString();
            using var response = await httpClient.GetAsync(finalUri, cts.Token);
            _logger.LogDebug("Push: {uri}", finalUri);
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
