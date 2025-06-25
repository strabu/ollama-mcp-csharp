using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using OllamaSharp;

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

string modelId = "qwen3:8b";
var ollama = new OllamaApiClient(new Uri("http://localhost:11434/"), modelId);
var chatClient = new ChatClientBuilder(ollama)
	.UseFunctionInvocation()
	.Build();

await foreach (var update in chatClient.GetStreamingResponseAsync("open orf.at and show me the response text", chatOptions))
{
	Console.Write(update.ToString());
}