using Microsoft.Extensions.AI;
using OllamaSharp;
using OllamaSharp.Models;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;

static string RunShellCommand(string command)
{	
	if (string.IsNullOrWhiteSpace(command))
		return "Error: command cannot be empty.";	

	var (fileName, arguments) = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
		//? ("cmd.exe", $"/c \"{command.Replace("\"", "\\\"")}\"")
		? ("wsl", $"bash -ic \"{command.Replace("\"", "\\\"")}\"")		
		: ("/bin/bash", $"-c \"{command.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"");

	Console.ForegroundColor = ConsoleColor.Cyan;
	Console.WriteLine();
	Console.WriteLine(command);
	Console.ResetColor();

	using var process = new Process
	{
		StartInfo = new ProcessStartInfo
		{
			FileName = fileName,
			Arguments = arguments,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true,
			WorkingDirectory = Environment.CurrentDirectory
		}
	};
	process.Start();
	var stdout = process.StandardOutput.ReadToEnd();
	Console.ForegroundColor = ConsoleColor.DarkCyan;
	Console.WriteLine(stdout);
	Console.ResetColor();
	var stderr = process.StandardError.ReadToEnd();
	Console.ForegroundColor = ConsoleColor.Red;
	Console.WriteLine(stderr);
	Console.ResetColor();
	process.WaitForExit(TimeSpan.FromSeconds(60));

	// Auto-handle consent dialogs after browser navigation
	if (command.Contains("agent-browser open") && stdout.Contains("consent", StringComparison.OrdinalIgnoreCase))
	{
		Console.ForegroundColor = ConsoleColor.Magenta;
		Console.WriteLine("[System] Detected consent page, auto-dismissing...");
		Console.ResetColor();

		var consentResult = RunConsentDismissal();
		stdout += "\n[System Auto-Consent] " + consentResult;
	}

	var result = new { ExitCode = process.ExitCode, Stdout = stdout, Stderr = stderr };
	return JsonSerializer.Serialize(result);
}

static string RunConsentDismissal()
{
	// Strategy 1: Set the Sourcepoint consent cookie and reload.
	// Most consent frameworks (Sourcepoint, OneTrust, etc.) use cookies to track consent state.
	// The _sp_su=true cookie tells Sourcepoint that the user has already consented.
	// This bypasses the cross-origin iframe problem entirely.
	string[] cookieCommands = [
		"agent-browser cookies set --name _sp_su --value true --domain .derstandard.at --path /",
		"agent-browser cookies set --name _sp_su --value true --domain .orf.at --path /",
		"agent-browser cookies set --name _sp_su --value true --path /",
	];

	foreach (var cmd in cookieCommands)
		RunShellCommand(cmd);

	// Reload the page so it picks up the consent cookie
	RunShellCommand("agent-browser reload");
	RunShellCommand("agent-browser wait 3000");
	return "Consent cookies set and page reloaded.";
}

ChatOptions chatOptions = new()
{
	Tools = [
		AIFunctionFactory.Create((string command) => RunShellCommand(command),
			"run_shell",
			$"Execute a shell command (e.g. agent-browser --help, agent-browser open example.com, agent-browser snapshot, agent-browser click @e2, ls/dir, git status, pwd/cd, cat/type file.txt) and return exit code, stdout and stderr. Use for any shell command.")
	]
};

//string modelId = "ministral-3:8b"; //the best
//string modelId = "qwen3:8b";
//string modelId = "qwen3.5:9b";
string modelId = "gemma4:e4b";

var ollamaUri = new Uri("http://localhost:11434/");
using var httpClient = new HttpClient(new DetailedHttpFailureHandler()) { BaseAddress = ollamaUri };
var ollama = new OllamaApiClient(httpClient, modelId);
var chatClient = new ChatClientBuilder(ollama)
	.ConfigureOptions(c => c.AddOllamaOption(OllamaOption.NumCtx, 16384))
	// .UseFunctionInvocation() // We will handle this manually
	.Build();

List<ChatMessage> messages =
[
	new(ChatRole.System, @"You are a helpful assistant with access to tools. 
ALWAYS use the run_shell tool to execute commands when the user asks you to interact with the file system, run programs, browse the web or perform any task that requires shell access.
Be creative and use the shell to get information for answers. 

When browsing the web:
1. Use 'agent-browser open <url>' to navigate. Cookie/consent dialogs are handled automatically.
2a. Run 'agent-browser screenshot [path]' to take a screenshot.
2b. Run 'agent-browser snapshot' to see the page structure and find element references (e.g., [ref=e12]).
3. Use 'agent-browser click @ref' to click elements by their reference ID from the snapshot.
4. Use 'agent-browser get text @ref' to extract text from elements.
5. IMPORTANT: After opening a page, always run 'agent-browser snapshot' to inspect the content and find what you need."),

			
	//new(ChatRole.User, "List files in the current directory")
	//new(ChatRole.User, "What time is it?")
	
	//new(ChatRole.User, "What are the tech-news headlines in derstandard.at? (accept/click consent if necessary)")
	new(ChatRole.User, "Browse to derstandard.at? Take a screenshot from the page and tell me what the pictures on the page show.")

	//new(ChatRole.User, "browse to derstandard.at, execute agent-browser screenshot page.png and tell me what you see on page.png")

	//new(ChatRole.User, "Get the News page from www.orf.at and make me a summary about Iran.")
	//new(ChatRole.User, "What's in the latest 13F filing from Berkshire Hathaway?")

];

while (true)
{
	bool calledTool = false;
	List<FunctionCallContent> functionCalls = [];
	string fullText = "";
	List<AIContent> fullContents = [];

	await foreach (var update in chatClient.GetStreamingResponseAsync(messages, chatOptions))
	{
		foreach (var content in update.Contents)
		{
			fullContents.Add(content);
			if (content is TextContent text)
			{
				Console.Write(text.Text);
				fullText += text.Text;
			}
			else if (content is FunctionCallContent fcc)
			{
				functionCalls.Add(fcc);
				calledTool = true;
			}
			else if (content is TextReasoningContent reasoning)
			{
				Console.ForegroundColor = ConsoleColor.Yellow;
				Console.Write(reasoning.Text);
				Console.ResetColor();
			}
		}
	}

	if (calledTool)
	{
		// Add the assistant's message with all contents (including reasoning) to the history
		if (fullContents.Count == 0)
			fullContents.Add(new TextContent("Calling tool..."));
		messages.Add(new ChatMessage(ChatRole.Assistant, fullContents));

		foreach (var item in functionCalls)
		{
			string command = item.Arguments != null && item.Arguments.TryGetValue("command", out object? cmdObj) ? cmdObj?.ToString() ?? "" : "";
			string toolResultJson = RunShellCommand(command);
			
			// Check if the command was a screenshot command
			AIContent? imageContent = null;
			if (command.Contains("screenshot"))
			{
				var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
				for (int i = 0; i < parts.Length; i++)
				{
					if (parts[i] == "screenshot" && i + 1 < parts.Length)
					{
						string fileName = parts[i + 1];
						if (File.Exists(fileName))
						{
							byte[] imageBytes = await File.ReadAllBytesAsync(fileName);
							imageContent = new DataContent(imageBytes, "image/png");
							Console.WriteLine($"\n[System] Attached image: {fileName}");
						}
						break;
					}
				}
			}

			var toolMessage = new ChatMessage(ChatRole.Tool, [new FunctionResultContent(item.CallId, toolResultJson)]);
			if (imageContent != null)
			{
				toolMessage.Contents.Add(imageContent);
			}
			messages.Add(toolMessage);
		}
	}
	else
	{
		messages.Add(new ChatMessage(ChatRole.Assistant, fullText));
		break;
	}
}



/// <summary>HTTP handler that throws with response body when status is not success (e.g. 500).</summary>
sealed class DetailedHttpFailureHandler : DelegatingHandler
{
	public DetailedHttpFailureHandler() : base(new HttpClientHandler()) { }

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var response = await base.SendAsync(request, cancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			var body = await response.Content.ReadAsStringAsync(cancellationToken);
			throw new HttpRequestException(
				$"Response status code does not indicate success: {(int)response.StatusCode} ({response.ReasonPhrase}). Response body: {body}");
		}
		return response;
	}
}