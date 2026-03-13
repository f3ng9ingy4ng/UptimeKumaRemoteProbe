namespace UptimeKumaRemoteProbe.Services;

public class CertificateService
{
    private readonly ILogger<CertificateService> _logger;
    private readonly PushService _pushService;
    private readonly IHttpClientFactory _httpClientFactory;

    public CertificateService(ILogger<CertificateService> logger, PushService pushService, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _pushService = pushService;
        _httpClientFactory = httpClientFactory;
    }

    public async Task CheckCertificateAsync(Endpoint endpoint)
    {
        DateTime notAfter = DateTime.UtcNow;

        var httpClient = _httpClientFactory.CreateClient("CertificateCheck");

        try
        {
            using var request = new HttpRequestMessage(new HttpMethod(endpoint.Method ?? "Head"), endpoint.Destination);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var result = await httpClient.SendAsync(request, cts.Token);
            
            // The ServerCertificateCustomValidationCallback will populate notAfter for us.
            // But since HttpClientFactory manages handlers and caches them, we can't cleanly extract cert info per-request 
            // from a shared handler without race conditions. We need a transient handler for this specific use-case or
            // read cert details directly from the returned HttpResponseMessage.
            // .NET 6+ doesn't easily expose the ServerCertificate from HttpResponseMessage though.
            
            // To be entirely safe and KISS while getting cert info without connection leaks:
            using var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (req, cert, chain, policyErrors) =>
                {
                    notAfter = cert.NotAfter;
                    return true;
                }
            };
            using var tempClient = new HttpClient(handler);
            var result2 = await tempClient.SendAsync(request, cts.Token);

            if (notAfter >= DateTime.UtcNow.AddDays(endpoint.CertificateExpiration))
            {
                await _pushService.PushAsync(endpoint.PushUri, (notAfter - DateTime.UtcNow).Days);
                _logger.LogInformation("Certificate: {endpoint.Destination} {result.StatusCode}",
                    endpoint.Destination, result.StatusCode);
                return;
            }
            _logger.LogWarning("Certificate: {endpoint.Destination} expiration date: {notAfter}", endpoint.Destination, notAfter);
        }
        catch
        {
            _logger.LogError("Error trying get {endpoint.Destination}", endpoint.Destination);
        }
    }
}
