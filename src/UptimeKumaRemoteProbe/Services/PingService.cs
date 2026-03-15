namespace UptimeKumaRemoteProbe.Services;

public class PingService
{
    private readonly ILogger<PingService> _logger;
    private readonly PushService _pushService;

    public PingService(ILogger<PingService> logger, PushService pushService)
    {
        _logger = logger;
        _pushService = pushService;
    }

    public async Task CheckPingAsync(Endpoint endpoint)
    {
        var destinations = endpoint.Destinations;

        bool anySuccess = false;
        long roundtripTime = 0;

        foreach (var destination in destinations)
        {
            using Ping ping = new();
            try
            {
                var pingReply = ping.Send(destination, endpoint.Timeout);
                _logger.LogInformation("Ping: {destination} {status}", destination, pingReply?.Status);

                if (pingReply?.Status == IPStatus.Success)
                {
                    anySuccess = true;
                    roundtripTime = pingReply.RoundtripTime;
                    break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error pinging {destination}", destination);
            }
        }

        if (anySuccess)
        {
            await _pushService.PushAsync(endpoint.PushUri, roundtripTime);
        }
    }
}
