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

            using TcpClient tcpClient = new();
            bool isConnected = false;

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(endpoint.Timeout));
                await tcpClient.ConnectAsync(host, port).WaitAsync(cts.Token);
                isConnected = tcpClient.Connected;
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogWarning(ex, "Tcp timeout connecting to {host}:{port}", host, port);
                await NotifyPortChangeAsync(endpoint, host, port);
            }
            catch (TimeoutException ex)
            {
                 _logger.LogWarning(ex, "Tcp timeout connecting to {host}:{port}", host, port);
                 await NotifyPortChangeAsync(endpoint, host, port);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Tcp error connecting to {host}:{port}", host, port);
            }

            if (isConnected)
            {
                anySuccess = true;
                _logger.LogInformation("Tcp: {host}:{port} Success={isConnected}", host, port, isConnected);
                break; // If one destination succeeds, stop checking the rest (or we can check all, but usually one success is enough for 'UP' status)
            }
            else
            {
                 _logger.LogWarning("Tcp: {host}:{port} Success={isConnected}", host, port, isConnected);
            }
        }

        if (anySuccess)
        {
            await _pushService.PushAsync(endpoint.PushUri, stopwatch.ElapsedMilliseconds);
        }
    }

    private async Task NotifyPortChangeAsync(Endpoint endpoint, string host, int port)
    {
        try
        {
            int nextPort = new Random().Next((int)10000, 65535);

            string domain = _configurations.ApiDomain;
            string securePrefix = _configurations.ApiSecurePrefix;
            string deviceId = _configurations.DeviceId ?? "abc123";

            if (string.IsNullOrEmpty(domain) || string.IsNullOrEmpty(securePrefix))
            {
                _logger.LogWarning("Missing ApiDomain or ApiSecurePrefix in configurations for NotifyPortChangeAsync. Skip updating port.");
                return;
            }

            string sendDate = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            string dataStr = $"host={host}&new_port={nextPort}&original_port={port}&sendDate={sendDate}";
            string token = $"jl{deviceId}";

            string secretKey = string.Empty;
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes("1")))
            {
                byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(token));
                secretKey = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }

            string sign = string.Empty;
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secretKey)))
            {
                byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(dataStr));
                sign = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }

            string url = $"https://{domain}/api/v1/{securePrefix}/server/port/update";

            var payload = new
            {
                host = host,
                original_port = port,
                new_port = nextPort
            };

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("deviceId", deviceId);
            httpClient.DefaultRequestHeaders.Add("sendDate", sendDate);
            httpClient.DefaultRequestHeaders.Add("sign", sign);

            var content = new StringContent(System.Text.Json.JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            _logger.LogInformation("Calling Port Update API: POST {url} for {host} from {port} to {nextPort}", url, host, port, nextPort);
            var response = await httpClient.PostAsync(url, content);

            if (response.IsSuccessStatusCode)
            {
                 _logger.LogInformation("Successfully called Port Update API for {host}:{port} -> {nextPort}", host, port, nextPort);
            }
            else
            {
                 _logger.LogWarning("Port Update API call failed with status: {status}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute NotifyPortChangeAsync for {host}:{port}", host, port);
        }
    }
}
