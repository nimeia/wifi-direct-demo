using System;

namespace WiFiDirectDemo.Protocol;

public sealed class ProtocolMessage
{
    public string Type { get; set; } = string.Empty;

    public string Sender { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;

    public static ProtocolMessage Hello(string sender, string text = "ready")
        => new ProtocolMessage
        {
            Type = "hello",
            Sender = sender,
            Text = text,
            TimestampUtc = DateTimeOffset.UtcNow,
        };

    public static ProtocolMessage Chat(string sender, string text)
        => new ProtocolMessage
        {
            Type = "chat",
            Sender = sender,
            Text = text,
            TimestampUtc = DateTimeOffset.UtcNow,
        };

    public static ProtocolMessage Ping(string sender, string text = "ping")
        => new ProtocolMessage
        {
            Type = "ping",
            Sender = sender,
            Text = text,
            TimestampUtc = DateTimeOffset.UtcNow,
        };
}
