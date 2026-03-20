namespace UptimeKumaRemoteProbe;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly Configurations _configurations;
    private readonly PingService _pingService;
    private readonly HttpService _httpService;
    private readonly TcpService _tcpService;
    private readonly CertificateService _certificateService;
    private readonly DbService _dbService;
    private readonly MonitorsService _monitorsService;
    private readonly AppSettings _appSettings;
    private readonly DomainService _domainService;
    private readonly VersionService _versionService;
    private readonly RealityService _realityService;
    private readonly IV2BoardService _v2BoardService;
    private static DateOnly lastDailyExecution;
    private long _loopCount = 0;

    public Worker(ILogger<Worker> logger, IConfiguration configuration, AppSettings appSettings, PingService pingService, HttpService httpService,
        TcpService tcpService, CertificateService certificateService, DbService dbService, MonitorsService monitorsService,
        DomainService domainService, VersionService versionService, RealityService realityService, IV2BoardService v2BoardService)
    {
        _logger = logger;
        _configurations = configuration.GetSection(nameof(Configurations)).Get<Configurations>();
        _appSettings = appSettings;
        _pingService = pingService;
        _httpService = httpService;
        _tcpService = tcpService;
        _certificateService = certificateService;
        _dbService = dbService;
        _monitorsService = monitorsService;
        _domainService = domainService;
        _versionService = versionService;
        _realityService = realityService;
        _v2BoardService = v2BoardService;
    }

    protected async override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogWarning("App version: {version}", Assembly.GetExecutingAssembly().GetName().Version.ToString());

        if (await _versionService.CheckVersionAsync())
        {
            Environment.Exit(0);
        }

        if (_appSettings.UpDependency == "")
        {
            _logger.LogError("Up Dependency is not set.");
            Environment.Exit(0);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            _loopCount++;
            _logger.LogDebug("--- Loop #{count} start ---", _loopCount);

            try
            {
                PingReply pingReply = null;

                if (_appSettings.UpDependency != "")
                {
                    try
                    {
                        using Ping ping = new();
                        pingReply = ping.Send(_appSettings.UpDependency, _appSettings.Timeout);
                        _logger.LogDebug("UpDependency ping {target}: {status} ({ms}ms)",
                            _appSettings.UpDependency, pingReply?.Status, pingReply?.RoundtripTime);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError("Network is unreachable. {ex}", ex.Message);
                    }
                }

                if (pingReply?.Status == IPStatus.Success)
                {
                    var monitors = await _monitorsService.GetMonitorsAsync();
                    if (monitors is not null)
                    {
                        var endpoints = ParseEndpoints(monitors);
                        _logger.LogDebug("Parsed {count} endpoints to check", endpoints.Count);
                        await LoopAsync(endpoints);
                    }
                    else
                    {
                        _logger.LogWarning("GetMonitorsAsync returned null, skipping this loop");
                    }
                }
                else
                {
                    _logger.LogError("Up Dependency is unreachable.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in main loop #{count}", _loopCount);
            }

            _logger.LogDebug("--- Loop #{count} end, next in {delay}ms ---", _loopCount, _appSettings.Delay);
            await Task.Delay(_appSettings.Delay, stoppingToken);
        }
    }

    private List<Endpoint> ParseEndpoints(List<Monitors> monitors)
    {
        var endpoints = new List<Endpoint>();
        bool hasProbeMonitor = false;

        foreach (var monitor in monitors)
        {
            var probe = monitor.Tags.Where(w => w.Name == "Probe").Select(s => s.Value).Contains(_appSettings.ProbeName);

            if (probe)
            {
                hasProbeMonitor = true;
            }

            if (monitor.Active && monitor.Maintenance is false && monitor.Type == "push" && probe)
            {
                var destinations = monitor.Tags.Where(w => w.Name == "Address").Select(s => s.Value).ToList();
                
                // V2Board metadata
                var v2boardApi = monitor.Tags.FirstOrDefault(t => t.Name == "V2Board_API")?.Value;
                var jwt = monitor.Tags.FirstOrDefault(t => t.Name == "V2Board_JWT")?.Value;
                var nodeTag = monitor.Tags.FirstOrDefault(t => t.Name == "V2Board_NodeTag")?.Value;
                var securePrefix = monitor.Tags.FirstOrDefault(t => t.Name == "V2Board_SecurePrefix")?.Value ?? _configurations.ApiSecurePrefix;
                var deviceId = monitor.Tags.FirstOrDefault(t => t.Name == "V2Board_DeviceId")?.Value ?? _configurations.DeviceId;

                var endpoint = new Endpoint
                {
                    Type = monitor.Tags.FirstOrDefault(t => t.Name == "Type")?.Value,
                    Destinations = destinations,
                    Timeout = int.Parse(monitor.Tags.FirstOrDefault(t => t.Name == "Timeout")?.Value ?? "1000"),
                    PushUri = new Uri($"{_appSettings.Url}api/push/{monitor.PushToken}?status=up&msg=OK_From_{_appSettings.ProbeName}&ping="),
                    Keyword = nodeTag ?? string.Empty, // Used for V2Board NodeTag
                    Method = jwt, // Used for V2Board JWT
                    Brand = monitor.Tags.FirstOrDefault(t => t.Name == "Brand")?.Value ?? string.Empty,
                    Port = int.Parse(monitor.Tags.FirstOrDefault(t => t.Name == "Port")?.Value ?? "0"),
                    Domain = v2boardApi ?? monitor.Tags.FirstOrDefault(t => t.Name == "Domain")?.Value ?? string.Empty,
                    SecurePrefix = securePrefix,
                    DeviceId = deviceId,
                    CertificateExpiration = int.Parse(monitor.Tags.FirstOrDefault(t => t.Name == "CertificateExpiration")?.Value ?? "3"),
                    IgnoreSSL = bool.Parse(monitor.Tags.FirstOrDefault(t => t.Name == "IgnoreSSL")?.Value ?? "False")
                };
                endpoints.Add(endpoint);
            }
        }

        if (!hasProbeMonitor)
        {
            _logger.LogWarning("No monitors with the specified Probe tag and value {_configurations.ProbeName} were found.", _appSettings.ProbeName);
        }

        return endpoints;
    }

    private async Task LoopAsync(List<Endpoint> endpoints)
    {
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount * 2
        };

        await Parallel.ForEachAsync(endpoints, options, async (item, token) =>
        {
            try
            {
                switch (item.Type)
                {
                    case "Ping":
                        await _pingService.CheckPingAsync(item);
                        break;
                    case "Http":
                        await _httpService.CheckHttpAsync(item);
                        break;
                    case "Tcp":
                        await _tcpService.CheckTcpAsync(item);
                        break;
                    case "Reality":
                        await _realityService.CheckRealityAsync(item);
                        break;
                    case "Certificate":
                        await _certificateService.CheckCertificateAsync(item);
                        break;
                    case "Database":
                        item.ConnectionString = $"{_configurations.ConnectionStrings}.{item.Brand}";
                        await _dbService.CheckDbAsync(item);
                        break;
                    case "Domain":
                        if (await CheckDailyExecutionAsync()) break;
                        await _domainService.CheckDomainAsync(item);
                        break;
                    default:
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking {type} endpoint {destination}", item.Type, item.Destinations.FirstOrDefault());
            }
        });
    }

    private static async Task<bool> CheckDailyExecutionAsync()
    {
        if (lastDailyExecution == DateOnly.FromDateTime(DateTime.Now))
        {
            return await Task.FromResult(true);
        }
        else
        {
            lastDailyExecution = DateOnly.FromDateTime(DateTime.Now);
            return await Task.FromResult(false);
        }
    }
}
