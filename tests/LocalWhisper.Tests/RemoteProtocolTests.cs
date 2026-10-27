using System.Text.Json;
using LocalWhisper.Services;

namespace LocalWhisper.Tests;

public class RemoteProtocolTests
{
    [Fact]
    public void Parse_Start_WithLanguage()
    {
        var c = RemoteProtocol.Parse("{\"type\":\"start\",\"language\":\"ru\"}");
        Assert.Equal(new RemoteCommand("start", "ru"), c);
    }

    [Theory]
    [InlineData("{\"type\":\"stop\"}", "stop")]
    [InlineData("{\"type\":\"cancel\"}", "cancel")]
    [InlineData("{\"type\":\"hello\",\"client\":\"clicky\"}", "hello")]
    public void Parse_SimpleCommands(string line, string type) =>
        Assert.Equal(type, RemoteProtocol.Parse(line)?.Type);

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"type\":\"shutdown\"}")]
    [InlineData("[1,2]")]
    public void Parse_RejectsUnknown(string line) => Assert.Null(RemoteProtocol.Parse(line));

    [Fact]
    public void Event_ComposesOneLineWithPayload()
    {
        var line = RemoteProtocol.Event("result", new { text = "привет", duration = 1.5 });
        Assert.DoesNotContain('\n', line);
        using var doc = JsonDocument.Parse(line);
        Assert.Equal("result", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal("привет", doc.RootElement.GetProperty("text").GetString());
    }
}
