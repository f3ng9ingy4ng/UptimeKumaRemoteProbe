namespace UptimeKumaRemoteProbe.Services;

public class TcpService
{
    private readonly ILogger<TcpService> _logger;
    private readonly PushService _pushService;
    private readonly Configurations _configurations;

    public TcpService(ILogger<TcpService> logger, PushService pushService, IOptions<Configurations> configurations)
    {
        _logger = logger;
        _pushService = pushService;
        _configurations = configurations.Value;
    }

    public async Task<(bool success, long ms)> CheckTcpOnlyAsync(string host, int port, int timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        using TcpClient tcpClient = new();
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeout));
            await tcpClient.ConnectAsync(host, port).WaitAsync(cts.Token);
            return (tcpClient.Connected, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("TcpOnly: {host}:{port} failed. {msg}", host, port, ex.Message);
            return (false, stopwatch.ElapsedMilliseconds);
        }
    }

    public async Task CheckTcpAsync(Endpoint endpoint)
    {
        var stopwatch = Stopwatch.StartNew();
        var destinations = endpoint.Destinations;
        bool anySuccess = false;

        foreach (var destUrl in destinations)
        {
            var host = destUrl;
            var port = endpoint.Port;

            if (destUrl.Contains(':'))
            {
                var parts = destUrl.Split(':');
                host = parts[0];
                if (parts.Length > 1 && int.TryParse(parts[1], out int parsedPort))
                {
                    port = parsedPort;
                }
            }

            var (success, _) = await CheckTcpOnlyAsync(host, port, endpoint.Timeout);

            if (success)
            {
                anySuccess = true;
                _logger.LogInformation("Tcp: {host}:{port} Success", host, port);
                break;
            }
            else
            {
                 _logger.LogWarning("Tcp: {host}:{port} Failed", host, port);
            }
        }

        if (anySuccess)
        {
            await _pushService.PushAsync(endpoint.PushUri, stopwatch.ElapsedMilliseconds);
        }
    }
}
