using System;
using WiFiDirectDemo.Protocol;
using Xunit;

namespace WiFiDirectDemo.Protocol.Tests;

public sealed class JsonLineProtocolTests
{
    [Fact]
    public void Serialize_And_Deserialize_RoundTrips()
    {
        var input = ProtocolMessage.Chat("tester", "hello");
        var json = JsonLineProtocol.Serialize(input);
        var output = JsonLineProtocol.Deserialize(json);

        Assert.Equal("chat", output.Type);
        Assert.Equal("tester", output.Sender);
        Assert.Equal("hello", output.Text);
    }

    [Fact]
    public void Deserialize_Throws_On_Empty_Input()
    {
        Assert.Throws<ArgumentException>(() => JsonLineProtocol.Deserialize(string.Empty));
    }

    [Fact]
    public void Factory_Hello_Produces_Hello_Type()
    {
        var msg = ProtocolMessage.Hello("host");

        Assert.Equal("hello", msg.Type);
        Assert.Equal("host", msg.Sender);
        Assert.True(msg.TimestampUtc <= DateTimeOffset.UtcNow);
    }
}
