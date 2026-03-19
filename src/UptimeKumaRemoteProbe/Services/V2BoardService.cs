namespace UptimeKumaRemoteProbe.Services;

public class V2BoardService : IV2BoardService
{
    private readonly ILogger<V2BoardService> _logger;
    private readonly HttpClient _httpClient;
    private readonly Configurations _configurations;

    public V2BoardService(ILogger<V2BoardService> logger, HttpClient httpClient, IOptions<Configurations> configurations)
    {
        _logger = logger;
        _httpClient = httpClient;
        _configurations = configurations.Value;
    }

    public async Task<List<V2BoardNode>> GetNodesAsync(string apiUrl, string jwt, string tagFilter)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{apiUrl.TrimEnd('/')}/api/v1/admin/server/v2ray/getNodes");
            request.Headers.Add("Authorization", jwt);

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<V2BoardResponse<List<V2BoardNode>>>(content);

            var nodes = result?.Data ?? new List<V2BoardNode>();

            if (!string.IsNullOrEmpty(tagFilter))
            {
                nodes = nodes.Where(n => n.Tags.Contains(tagFilter, StringComparer.OrdinalIgnoreCase)).ToList();
            }

            return nodes;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get nodes from V2Board API: {apiUrl}", apiUrl);
            return new List<V2BoardNode>();
        }
    }

    public async Task UpdatePortAsync(string apiUrl, string jwt, string host, int originalPort, int newPort)
    {
        try
        {
            string domain = _configurations.ApiDomain;
            string securePrefix = _configurations.ApiSecurePrefix;
            string deviceId = _configurations.DeviceId ?? "abc123";

            if (string.IsNullOrEmpty(domain) || string.IsNullOrEmpty(securePrefix))
            {
                _logger.LogWarning("Missing ApiDomain or ApiSecurePrefix in configurations. Redirecting to manual V2Board update if possible.");
                // Note: The logic from update_port.sh uses a specific signing mechanism.
            }

            string sendDate = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            string dataStr = $"host={host}&new_port={newPort}&original_port={originalPort}&sendDate={sendDate}";
            string token = $"jl{deviceId}";

            string secretKey;
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes("1")))
            {
                byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(token));
                secretKey = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }

            string sign;
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secretKey)))
            {
                byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(dataStr));
                sign = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }

            string url = $"https://{domain}/api/v1/{securePrefix}/server/port/update";

            var payload = new
            {
                host = host,
                original_port = originalPort,
                new_port = newPort
            };

            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("deviceId", deviceId);
            request.Headers.Add("sendDate", sendDate);
            request.Headers.Add("sign", sign);
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            _logger.LogInformation("Calling V2Board Port Update API: POST {url} for {host}:{originalPort} -> {newPort}", url, host, originalPort, newPort);
            
            var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Successfully updated port for {host}:{originalPort} to {newPort}", host, originalPort, newPort);
            }
            else
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("V2Board Port Update API failed: {status}, Body: {body}", response.StatusCode, errorBody);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing V2Board UpdatePortAsync for {host}:{originalPort}", host, originalPort);
        }
    }
}
