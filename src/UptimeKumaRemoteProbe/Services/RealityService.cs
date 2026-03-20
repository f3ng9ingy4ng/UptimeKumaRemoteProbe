namespace UptimeKumaRemoteProbe.Services;

using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;

public class RealityService
{
    private readonly ILogger<RealityService> _logger;
    private readonly TcpService _tcpService;
    private readonly PingService _pingService;
    private readonly IV2BoardService _v2BoardService;
    private readonly PushService _pushService;
    private readonly AppSettings _appSettings;

    public RealityService(ILogger<RealityService> logger, TcpService tcpService, PingService pingService, 
        IV2BoardService v2BoardService, PushService pushService, AppSettings appSettings)
    {
        _logger = logger;
        _tcpService = tcpService;
        _pingService = pingService;
        _v2BoardService = v2BoardService;
        _pushService = pushService;
        _appSettings = appSettings;
    }

    public async Task CheckRealityAsync(Endpoint endpoint)
    {
        var stopwatch = Stopwatch.StartNew();
        var results = new List<string>();
        var switchLogs = new List<string>();
        var blockedNodes = new List<string>();
        bool anyIpBlocked = false;
        bool anyCheckPerformed = false;
        long maxMs = 0;

        // 1. Fetch nodes from V2Board
        var allNodes = await _v2BoardService.GetNodesAsync(endpoint.Domain, endpoint.Method, endpoint.SecurePrefix, endpoint.Keyword);

        // 2. Filter by Region matching (logic moved from Worker)
        var matchingNodes = allNodes.Where(n => 
        {
            var regionTag = n.Tags.FirstOrDefault(t => t.StartsWith("region:", StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(regionTag)) return true;
            
            var regionValue = regionTag.Split(':').LastOrDefault()?.Trim();
            return string.Equals(regionValue, _appSettings.ProbeName, StringComparison.OrdinalIgnoreCase);
        }).ToList();

        if (!matchingNodes.Any())
        {
            _logger.LogWarning("Reality: No nodes matched for Probe {probe} and Tag {tag}", _appSettings.ProbeName, endpoint.Keyword);
            return;
        }

        // 3. Check each node
        foreach (var node in matchingNodes)
        {
            anyCheckPerformed = true;
            var host = node.Host;
            var port = node.Port;

            _logger.LogInformation("Reality: Checking node {name} ({host}:{port})", node.Name, host, port);
            var (tcpSuccess, ms) = await _tcpService.CheckTcpOnlyAsync(host, port, endpoint.Timeout);
            maxMs = Math.Max(maxMs, ms);

            if (tcpSuccess)
            {
                results.Add($"{node.Name}: OK");
                continue;
            }

            // TCP Failed, check IP
            _logger.LogWarning("Reality: TCP failed for {name}, checking Ping...", node.Name);
            var pingSuccess = await _pingService.CheckPingOnlyAsync(host, endpoint.Timeout);

            if (pingSuccess)
            {
                // Port Blocked -> Update
                _logger.LogWarning("Reality: Port Blocked on {name}! Attempting update...", node.Name);
                int nextPort = new Random().Next(10000, 65535);
                await _v2BoardService.UpdatePortAsync(endpoint.Domain, endpoint.Method, endpoint.SecurePrefix, endpoint.DeviceId, host, port, nextPort);
                
                switchLogs.Add($"{node.Name}: {port}->{nextPort}");
                results.Add($"{node.Name}: Port Switched");
            }
            else
            {
                // IP Blocked
                _logger.LogError("Reality: IP Blocked on {name} ({host})", node.Name, host);
                anyIpBlocked = true;
                blockedNodes.Add(node.Name);
                results.Add($"{node.Name}: IP BLOCKED");
            }
        }

        if (!anyCheckPerformed) return;

        // 4. Final Push Aggregation
        // Requirement: ANY IP Blocked = DOWN, otherwise UP
        string message;
        string status = anyIpBlocked ? "down" : "up";

        if (anyIpBlocked)
        {
            var blockedList = string.Join(", ", blockedNodes);
            message = $"CRITICAL: IP Blocked on [{blockedList}]";
        }
        else
        {
            message = switchLogs.Any() ? $"OK (Switched: {string.Join("; ", switchLogs)})" : "All nodes OK";
        }

        // Append full results for better visibility in Kuma
        message += $" | Details: {string.Join(", ", results)}";

        // Separate msg and ping parameters
        // Example base: http://kuma/api/push/token
        var baseUrl = endpoint.PushUri.GetLeftPart(UriPartial.Path);
        var finalPushUrl = $"{baseUrl}?status={status}&msg={Uri.EscapeDataString(message)}&ping={maxMs}";
        
        await _pushService.PushAsync(new Uri(finalPushUrl), null);
    }
}
