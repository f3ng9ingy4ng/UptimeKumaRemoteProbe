namespace UptimeKumaRemoteProbe.Models;

public class V2BoardNode
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("host")]
    public string Host { get; set; }

    [JsonPropertyName("port")]
    public int Port { get; set; }

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();
}

public class V2BoardResponse<T>
{
    [JsonPropertyName("data")]
    public T Data { get; set; }
}
