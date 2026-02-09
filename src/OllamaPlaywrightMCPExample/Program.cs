using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using OllamaSharp;
using System.Text.Json;

//https://github.com/microsoft/playwright-mcp
var clientTransport = new StdioClientTransport(new StdioClientTransportOptions
{
	Name = "PlaywrightMCP",
	Command = "npx",
	Arguments = ["-y", "@playwright/mcp@latest"]
});

var mcpClient = await McpClientFactory.CreateAsync(clientTransport);
var tools = await mcpClient.ListToolsAsync();
ChatOptions chatOptions = new()
{
	Tools = tools.ToArray()
};

//string modelId = "qwen3:8b";
//string modelId = "lfm2.5-thinking:latest"; fast, calls browser, does not understand reponse
//string modelId = "qwen3-vl:2b"; //does not really call the tools
string modelId = "ministral-3:8b"; //works
var ollama = new OllamaApiClient(new Uri("http://localhost:11434/"), modelId);
var chatClient = new ChatClientBuilder(ollama)
	.UseFunctionInvocation()
	.Build();

await foreach (var update in chatClient.GetStreamingResponseAsync("1) navigate to url https://www.orf.at 2) extract the 3 top news headlines of heading: 'Ausland' 3) only show me the top 3 headlines ignore logs and errors.", chatOptions))
{
	LogToolCalls(update);

	// Write regular text content
	Console.Write(update.ToString());
}

static void LogToolCalls(ChatResponseUpdate update)
{
	// Log tool calls and their results in color
	if (update.Contents.Any(c => c is FunctionCallContent || c is FunctionResultContent))
	{
		foreach (var content in update.Contents)
		{
			if (content is FunctionCallContent toolCall)
			{
				Console.ForegroundColor = ConsoleColor.Yellow;
				Console.WriteLine($"\n{'=',-60}");
				Console.WriteLine($"[Tool Call] {toolCall.Name}");
				Console.WriteLine($"{'=',-60}");
				Console.WriteLine("Arguments:");
				Console.WriteLine(FormatJson(toolCall.Arguments));
				Console.ResetColor();
			}
			else if (content is FunctionResultContent toolResult)
			{
				Console.ForegroundColor = ConsoleColor.Cyan;
				Console.WriteLine($"\n{'=',-60}");
				Console.WriteLine($"[Tool Result] {toolResult.CallId}");
				Console.WriteLine($"{'=',-60}");
				Console.WriteLine("Result:");
				Console.WriteLine(FormatResult(toolResult.Result));
				Console.ResetColor();
			}
		}
	}
}

static string FormatJson(object? obj)
{
	if (obj == null) return "null";

	try
	{
		var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions
		{
			WriteIndented = true,
			Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
		});
		return IndentText(json, 2);
	}
	catch
	{
		return IndentText(obj.ToString() ?? "null", 2);
	}
}

static string FormatResult(object? result)
{
	if (result == null) return "  null";

	var resultStr = result.ToString() ?? "";

	// Try to parse as JSON first
	try
	{
		using var doc = JsonDocument.Parse(resultStr);
		var formatted = JsonSerializer.Serialize(doc, new JsonSerializerOptions
		{
			WriteIndented = true,
			Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
		});

		// Replace escaped newlines with actual newlines
		formatted = formatted.Replace("\\n", "\n");

		// Also unescape other common escape sequences
		formatted = formatted.Replace("\\t", "\t");
		formatted = formatted.Replace("\\r", "\r");

		return IndentText(formatted, 2);
	}
	catch
	{
		// Not JSON, just indent it (and unescape newlines)
		resultStr = resultStr.Replace("\\n", "\n").Replace("\\t", "\t").Replace("\\r", "\r");
		return IndentText(resultStr, 2);
	}
}

static string IndentText(string text, int spaces)
{
	var indent = new string(' ', spaces);
	return indent + text.Replace("\n", "\n" + indent);
}