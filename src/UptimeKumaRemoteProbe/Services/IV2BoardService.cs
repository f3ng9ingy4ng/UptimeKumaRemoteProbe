namespace UptimeKumaRemoteProbe.Services;

public interface IV2BoardService
{
    Task<List<V2BoardNode>> GetNodesAsync(string apiUrl, string jwt, string securePrefix, string tagFilter);
    Task UpdatePortAsync(string apiUrl, string jwt, string securePrefix, string deviceId, string host, int originalPort, int newPort);
}
