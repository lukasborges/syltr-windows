using Syltr.Engine;

namespace Syltr.Tests.Engine;

public sealed class WebConsoleMessageParserTests
{
    [Fact]
    public void Parses_console_arguments_and_keeps_only_the_source_origin()
    {
        const string payload = """
            {
              "type": "error",
              "args": [
                { "type": "string", "value": "request failed" },
                { "type": "number", "value": 503 },
                { "type": "object", "description": "Error: unavailable" }
              ],
              "stackTrace": {
                "callFrames": [
                  {
                    "url": "https://chat.example.com/app.js?token=secret",
                    "lineNumber": 9,
                    "columnNumber": 4
                  }
                ]
              }
            }
            """;

        var parsed = WebConsoleMessageParser.TryParse("chat-work", payload, out var message);

        Assert.True(parsed);
        Assert.Equal(ServiceConsoleMessageLevel.Error, message.Level);
        Assert.Equal("request failed 503 Error: unavailable", message.Message);
        Assert.Equal("https://chat.example.com/", message.SourceOrigin?.ToString());
        Assert.Equal(10, message.LineNumber);
        Assert.Equal(5, message.ColumnNumber);
        Assert.DoesNotContain("secret", message.SourceOrigin?.ToString());
    }

    [Fact]
    public void Rejects_invalid_protocol_payloads()
    {
        Assert.False(WebConsoleMessageParser.TryParse("profile", "not json", out _));
        Assert.False(WebConsoleMessageParser.TryParse("", "{}", out _));
    }

    [Fact]
    public void Truncates_large_console_messages()
    {
        var payload = $$"""{"type":"log","args":[{"type":"string","value":"{{new string('x', 5000)}}"}]}""";

        Assert.True(WebConsoleMessageParser.TryParse("profile", payload, out var message));
        Assert.Equal(4097, message.Message.Length);
        Assert.EndsWith("…", message.Message);
    }
}
