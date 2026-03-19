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

    public async Task<bool> CheckPingOnlyAsync(string destination, int timeout)
    {
        using Ping ping = new();
        try
        {
            var pingReply = ping.Send(destination, timeout);
            _logger.LogInformation("PingOnly: {destination} {status}", destination, pingReply?.Status);
            return pingReply?.Status == IPStatus.Success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error pinging {destination}", destination);
            return false;
        }
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
