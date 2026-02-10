using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using OllamaSharp;
using System.Text.Json;

Console.WriteLine("Starting Ollama Playwright MCP Example");

//https://github.com/microsoft/playwright-mcp
var clientTransport = new StdioClientTransport(new StdioClientTransportOptions
{
	Name = "PlaywrightMCP",
	Command = "npx",
	Arguments = ["-y", "@playwright/mcp@latest"]
});

//var mcpClient = await McpClientFactory.CreateAsync(clientTransport);
var mcpClient = await McpClient.CreateAsync(clientTransport);

var tools = await mcpClient.ListToolsAsync();
ChatOptions chatOptions = new()
{
	Tools = tools.ToArray(),
	Temperature = 0.1f // Lower temperature for more focused, deterministic responses
};

//string modelId = "qwen2.5:14b"; // Try a larger model if available, or qwen2.5:7b
//string modelId = "llama3.1:latest"; // Generally good instruction following
string modelId = "ministral-3:8b"; // worked on macbook air
//string modelId = "qwen2.5:7b"; // Switching to Qwen 2.5 which often handles context better than Ministral
//string modelId = "gpt-oss:20b";
 
var ollama = new OllamaApiClient(new Uri("http://localhost:11434/"), modelId);
var chatClient = new ChatClientBuilder(ollama)
	.UseFunctionInvocation()
	.Build();

List<ChatMessage> messages = new()
{
	new ChatMessage(ChatRole.System, "You are a news extractor. Your ONLY job is to extract headlines. You are NOT a debugger. You are NOT a website analyzer. Ignore all console logs, errors, and HTML structure details."),
	new ChatMessage(ChatRole.User, @"GOAL: Extract 3 headlines from the 'Ausland' section of orf.at.

IMPORTANT: The 'navigate' tool ONLY returns console logs. It does NOT return the page text.
You MUST perform these steps in order:

STEP 1: Navigate to https://www.orf.at
STEP 2: Call the 'evaluate' tool with the javascript ""document.body.innerText"" to get the actual text content of the page.
STEP 3: Read the text content from Step 2 (ignore the logs from Step 1).
STEP 4: Find the 'Ausland' section in that text and extract the top 3 headlines.

OUTPUT RULES:
- Output ONLY the 3 headlines.
- Do NOT summarize the console logs.
- Do NOT explain what you found.

Final Answer Format:
- Headline 1
- Headline 2
- Headline 3")
};

await foreach (var update in chatClient.GetStreamingResponseAsync(messages, chatOptions))
{
	// Only log tool calls if you want to see debugging info
	LogToolCalls(update);

	// Only write text content (not tool calls/results)
	if (update.Contents.Any(c => c is TextContent))
	{
		foreach (var content in update.Contents.OfType<TextContent>())
		{
			Console.Write(content.Text);
		}
	}
}

// Uncomment the LogToolCalls line above if you want to see debugging information about tool calls
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