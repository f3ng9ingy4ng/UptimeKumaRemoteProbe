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
    [JsonConverter(typeof(IntOrStringConverter))]
    public int Port { get; set; }

    [JsonPropertyName("server_port")]
    public int ServerPort { get; set; }

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();

    [JsonPropertyName("show")]
    public int Show { get; set; }
}

public class IntOrStringConverter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number) return reader.GetInt32();
        if (reader.TokenType == JsonTokenType.String && int.TryParse(reader.GetString(), out int result)) return result;
        return 0;
    }

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value);
    }
}

public class V2BoardResponse<T>
{
    [JsonPropertyName("data")]
    public T Data { get; set; }
}
