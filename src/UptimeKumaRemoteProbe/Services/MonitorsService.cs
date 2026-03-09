namespace UptimeKumaRemoteProbe.Services;

public class MonitorsService
{
    private readonly ILogger<MonitorsService> _logger;
    private readonly AppSettings _appSettings;

    public MonitorsService(ILogger<MonitorsService> logger, AppSettings appSettings)
    {
        _logger = logger;
        _appSettings = appSettings;
    }

    public async Task<List<Monitors>> GetMonitorsAsync()
    {
        SocketIOClient.SocketIO socket = null;
        try
        {
            socket = new SocketIOClient.SocketIO(_appSettings.Url, new SocketIOClient.SocketIOOptions
            {
                ReconnectionAttempts = 3,
                ConnectionTimeout = TimeSpan.FromSeconds(15)
            });

            var data = new
            {
                username = _appSettings.Username,
                password = _appSettings.Password,
                token = ""
            };

            JsonElement monitorsRaw = new();
            bool loginSuccess = false;

            socket.On("monitorList", response =>
            {
                monitorsRaw = response.GetValue<JsonElement>();
                _logger.LogDebug("Received monitorList event from server");
            });

            socket.OnConnected += async (sender, e) =>
            {
                _logger.LogInformation("SocketIO connected to {url}", _appSettings.Url);
                await socket.EmitAsync("login", (ack) =>
                {
                    var result = JsonNode.Parse(ack.GetValue<JsonElement>(0).ToString());
                    if (result["ok"].ToString() != "true")
                    {
                        _logger.LogError("Uptime Kuma login failure, response: {result}", result.ToString());
                    }
                    else
                    {
                        loginSuccess = true;
                        _logger.LogDebug("Uptime Kuma login success");
                    }
                }, data);
            };

            socket.OnDisconnected += (sender, e) =>
            {
                _logger.LogDebug("SocketIO disconnected: {reason}", e);
            };

            socket.OnError += (sender, e) =>
            {
                _logger.LogError("SocketIO error: {error}", e);
            };

            _logger.LogDebug("SocketIO connecting to {url}...", _appSettings.Url);

            // ConnectAsync with 30s timeout
            using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await socket.ConnectAsync(connectCts.Token);

            // Wait for monitorList data, max 10 seconds
            int round = 0;
            while (monitorsRaw.ValueKind == JsonValueKind.Undefined)
            {
                round++;
                await Task.Delay(1000);
                if (round >= 10)
                {
                    _logger.LogWarning("Timed out waiting for monitorList after {seconds}s (loginSuccess={loginSuccess})", round, loginSuccess);
                    break;
                }
            }

            // DisconnectAsync without cancellation token as it's not supported in this version
            _logger.LogDebug("SocketIO disconnecting...");
            try
            {
                await socket.DisconnectAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("SocketIO disconnect failed: {error}", ex.Message);
            }

            if (monitorsRaw.ValueKind == JsonValueKind.Undefined)
            {
                _logger.LogWarning("No monitorList data received from server");
                return null;
            }

            var monitors = JsonSerializer.Deserialize<Dictionary<string, Monitors>>(monitorsRaw);
            _logger.LogInformation("Fetched {count} monitors from Uptime Kuma", monitors?.Count ?? 0);
            return monitors.Values.ToList();
        }
        catch (OperationCanceledException)
        {
            _logger.LogError("SocketIO connect timed out after 30s to {url}", _appSettings.Url);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError("Error trying to get monitors: {error}", ex.Message);
            return null;
        }
        finally
        {
            if (socket != null)
            {
                try { socket.Dispose(); } catch { /* ignore */ }
            }
        }
    }
}
