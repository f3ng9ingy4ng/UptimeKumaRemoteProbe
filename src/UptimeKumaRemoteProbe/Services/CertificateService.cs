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
        var destinations = endpoint.Destinations;

        bool anySuccess = false;
        int remainingDays = 0;

        foreach (var destination in destinations)
        {
            DateTime notAfter = DateTime.UtcNow;
            try
            {
                using var request = new HttpRequestMessage(new HttpMethod(endpoint.Method ?? "Head"), destination);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                
                using var handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (req, cert, chain, policyErrors) =>
                    {
                        if (cert != null)
                        {
                            notAfter = cert.NotAfter;
                        }
                        return true;
                    }
                };

                using var tempClient = new HttpClient(handler);
                var result = await tempClient.SendAsync(request, cts.Token);

                _logger.LogInformation("Certificate: {destination} status: {statusCode}", destination, result.StatusCode);

                if (notAfter >= DateTime.UtcNow.AddDays(endpoint.CertificateExpiration))
                {
                    anySuccess = true;
                    remainingDays = (notAfter - DateTime.UtcNow).Days;
                    break;
                }
                else
                {
                    _logger.LogWarning("Certificate for {destination} expiration date: {notAfter} is too close or expired.", destination, notAfter);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error trying to get certificate for {destination}", destination);
            }
        }

        if (anySuccess)
        {
            await _pushService.PushAsync(endpoint.PushUri, remainingDays);
        }
    }
}
