using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WiFiDirectDemo.Protocol;

public static class JsonLineProtocol
{
    private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public static string Serialize(ProtocolMessage message)
    {
        if (message is null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        return JsonSerializer.Serialize(message, SerializerOptions);
    }

    public static ProtocolMessage Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("JSON payload cannot be null or whitespace.", nameof(json));
        }

        var message = JsonSerializer.Deserialize<ProtocolMessage>(json, SerializerOptions);
        return message ?? throw new InvalidDataException("Failed to deserialize protocol message.");
    }

    public static async Task WriteLineAsync(Stream stream, ProtocolMessage message, CancellationToken cancellationToken = default)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        var json = Serialize(message) + "\n";
        var data = Encoding.UTF8.GetBytes(json);
        await stream.WriteAsync(data, 0, data.Length, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
