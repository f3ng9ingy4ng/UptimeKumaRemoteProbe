namespace UptimeKumaRemoteProbe.Services;

using System.Web;

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

    public async Task<List<V2BoardNode>> GetNodesAsync(string apiUrl, string jwt, string securePrefix, string tagFilter)
    {
        if (string.IsNullOrEmpty(apiUrl))
        {
            _logger.LogError("apiUrl is missing. Cannot fetch nodes.");
            return new List<V2BoardNode>();
        }

        if (string.IsNullOrEmpty(securePrefix))
        {
            _logger.LogError("securePrefix is missing! Construction of V2Board API URL will fail. Please set V2Board_SecurePrefix tag or ApiSecurePrefix in config.");
            return new List<V2BoardNode>();
        }

        try
        {
            // Construct URL: /api/v1/{securePrefix}/server/stats?show=1&tag={tagFilter}
            var baseUrl = $"{apiUrl.TrimEnd('/')}/api/v1/{securePrefix}/server/stats";
            var uriBuilder = new UriBuilder(baseUrl);
            var query = HttpUtility.ParseQueryString(uriBuilder.Query);
            query["show"] = "1";
            if (!string.IsNullOrEmpty(tagFilter))
            {
                query["tag"] = tagFilter;
            }
            uriBuilder.Query = query.ToString();

            // UriBuilder might add default ports (80/443), sometimes V2Board is sensitive. 
            // Let's use the UriBuilder's Uri directly but be aware.
            var finalUrl = uriBuilder.Uri.ToString();
            _logger.LogInformation("V2Board Request: GET {url}", finalUrl);

            var request = new HttpRequestMessage(HttpMethod.Get, finalUrl);
            request.Headers.Add("Authorization", jwt);

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _logger.LogError("V2Board API Error: {status} for {url}. Response: {body}", response.StatusCode, finalUrl, errorBody);
                return new List<V2BoardNode>();
            }

            var content = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<V2BoardResponse<List<V2BoardNode>>>(content);

            var nodes = result?.Data ?? new List<V2BoardNode>();
            return nodes.Where(n => n.Show == 1).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error fetching nodes from V2Board: {apiUrl}", apiUrl);
            return new List<V2BoardNode>();
        }
    }

    public async Task UpdatePortAsync(string apiUrl, string jwt, string securePrefix, string deviceId, string host, int originalPort, int newPort)
    {
        try
        {
            if (string.IsNullOrEmpty(apiUrl) || string.IsNullOrEmpty(securePrefix))
            {
                _logger.LogWarning("Missing apiUrl or securePrefix. Skip update.");
                return;
            }

            string sendDate = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            string dataStr = $"host={host}&new_port={newPort}&original_port={originalPort}&sendDate={sendDate}";
            string token = $"jl{deviceId ?? "abc123"}";

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

            string url = $"{apiUrl.TrimEnd('/')}/api/v1/{securePrefix}/server/port/update";

            var payload = new
            {
                host = host,
                original_port = originalPort,
                new_port = newPort
            };

            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("Authorization", jwt);
            request.Headers.Add("deviceId", deviceId ?? "abc123");
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
