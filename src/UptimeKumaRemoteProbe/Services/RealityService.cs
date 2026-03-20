namespace UptimeKumaRemoteProbe.Services;

public class RealityService
{
    private readonly ILogger<RealityService> _logger;
    private readonly TcpService _tcpService;
    private readonly PingService _pingService;
    private readonly IV2BoardService _v2BoardService;
    private readonly PushService _pushService;

    public RealityService(ILogger<RealityService> logger, TcpService tcpService, PingService pingService, 
        IV2BoardService v2BoardService, PushService pushService)
    {
        _logger = logger;
        _tcpService = tcpService;
        _pingService = pingService;
        _v2BoardService = v2BoardService;
        _pushService = pushService;
    }

    public async Task CheckRealityAsync(Endpoint endpoint)
    {
        var stopwatch = Stopwatch.StartNew();
        bool anySuccess = false;
        long totalMs = 0;

        foreach (var destination in endpoint.Destinations)
        {
            var host = destination;
            var port = endpoint.Port;

            if (destination.Contains(':'))
            {
                var parts = destination.Split(':');
                host = parts[0];
                if (parts.Length > 1 && int.TryParse(parts[1], out int parsedPort))
                {
                    port = parsedPort;
                }
            }

            _logger.LogInformation("Reality: Checking {host}:{port}", host, port);
            var (tcpSuccess, ms) = await _tcpService.CheckTcpOnlyAsync(host, port, endpoint.Timeout);

            if (tcpSuccess)
            {
                anySuccess = true;
                totalMs = ms;
                _logger.LogInformation("Reality: {host}:{port} is UP", host, port);
                break;
            }

            // TCP Failed, check if IP is pingable
            _logger.LogWarning("Reality: TCP failed for {host}:{port}, checking Ping...", host, port);
            var pingSuccess = await _pingService.CheckPingOnlyAsync(host, endpoint.Timeout);

            if (pingSuccess)
            {
                _logger.LogWarning("Reality: Port Blocked Detected! (IP is pingable, but TCP failed) for {host}:{port}", host, port);
                
                // Switch port
                int nextPort = new Random().Next(10000, 65535);
                await _v2BoardService.UpdatePortAsync(endpoint.Domain, endpoint.Method, endpoint.SecurePrefix, endpoint.DeviceId, host, port, nextPort);
            }
            else
            {
                _logger.LogError("Reality: IP Blocked Detected! (IP is unreachable) for {host}", host);
            }
        }

        if (anySuccess)
        {
            await _pushService.PushAsync(endpoint.PushUri, totalMs);
        }
        else
        {
            _logger.LogWarning("Reality: All destinations failed for {pushUri}", endpoint.PushUri);
        }
    }
}
