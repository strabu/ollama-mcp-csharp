using Microsoft.Extensions.AI;
using OllamaSharp;
using OllamaSharp.Models;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using OllamaToolCallingExample;
using OllamaToolCallingExample.Skills;

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

static string GetSkills()
{
	return "*browser*: use the local webbrowser\r\n*file*: read or write files";
}

static string LearnSkill(string command)
{
	return @"When browsing the web use run_shell and then:
1. Use 'agent-browser open <url>' to navigate. Cookie/consent dialogs are handled automatically.
2a. Run 'agent-browser screenshot [path]' to take a screenshot.
2b. Run 'agent-browser snapshot' to see the page structure and find element references (e.g., [ref=e12]).
3. Use 'agent-browser click @ref' to click elements by their reference ID from the snapshot.
4. Use 'agent-browser get text @ref' to extract text from elements.
5. IMPORTANT: After opening a page, always run 'agent-browser snapshot' to inspect the content and find what you need.";
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
		/*
		AIFunctionFactory.Create((string getSkills) => GetSkills(),
			"get_skills",
			$"Returns the list of ALL Capabilities"),
		*/
		AIFunctionFactory.Create((string learnSkillCommand) => LearnSkill(learnSkillCommand),
			"learn_skill",
			$"Returns the details for a skill and how to use it"),
		
		
		AIFunctionFactory.Create((string command) => RunShellCommand(command),
			"run_shell",
			$"Execute a shell command and return exit code, stdout and stderr. Use for any shell command."),
		
	]
};


//https://agentskills.io/client-implementation/adding-skills-support#frontmatter-extraction
//https://github.com/dotnet/extensions/tree/main/src/Libraries/Microsoft.Extensions.AI.Abstractions

//string modelId = "ministral-3:8b"; //the best
//string modelId = "qwen3:8b";
//string modelId = "qwen3.5:9b";
string modelId = "gemma4:e4b";

var ollamaUri = new Uri("http://localhost:11434/");
using var httpClient = new HttpClient(new DetailedHttpFailureHandler()) { BaseAddress = ollamaUri };
var ollama = new OllamaApiClient(httpClient, modelId);
var chatClient = new ChatClientBuilder(ollama)
	.ConfigureOptions(c => c.AddOllamaOption(OllamaOption.NumCtx, 16384))
	//.ConfigureOptions(c => c.AddOllamaOption(OllamaOption.NumCtx, 32768))
	// .UseFunctionInvocation() // We will handle this manually
	.UseConsoleLogger()
	.UseSkills(configure: c => c.ShellCommand = RunShellCommand)
	.Build();

List<ChatMessage> messages =
[
	new(ChatRole.System, $@"You are a helpful assistant with access to many tools and skills. Skills are **NOT** TOOLS. You *must* always call learn_skill(skillName) before you can use a skill. These are the skills (NOT tools): {GetSkills()}"),

			
	//new(ChatRole.User, "List files in the current directory")
	//new(ChatRole.User, "What time is it?")
	
	//new(ChatRole.User, "What are the tech-news headlines in derstandard.at? (accept/click consent if necessary)")
	
	//new(ChatRole.User, "Browse to derstandard.at? Take a screenshot from the page and tell me what the pictures on the page show.")
	new(ChatRole.User, "Browse to derstandard.at/web ? Take a screenshot from the page and tell me what the pictures on the page show.")

//new(ChatRole.User, "Search yahoo finance for the latest SEC 13f-filing from Berkshire Hathaway.")

	//new(ChatRole.User, "Get the News page from www.orf.at and make me a summary about Iran.")
	//new(ChatRole.User, "What's in the latest 13F filing from Berkshire Hathaway?")

];


//while (true)
//{
	await foreach (var update in chatClient.GetStreamingResponseAsync(messages, chatOptions))
	{
		var u = update;
		if (update.Role == ChatRole.Tool)
		{
			var re = (FunctionResultContent)update.Contents.First();
			var rs = re.Result as string;
			messages.Add(new ChatMessage(ChatRole.Tool, rs));
		}
		else if (update.Role == ChatRole.Assistant)
		{
			if (!string.IsNullOrEmpty(update.Text))
			{
				var rs = update.Text;
				messages.Add(new ChatMessage(ChatRole.Assistant, rs));
			}
		}
	}
//}
/*
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
				fullText += text.Text;
			}
			else if (content is FunctionCallContent fcc)
			{
				functionCalls.Add(fcc);
				calledTool = true;
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
			if (item.Name == "learn_skill")
			{
				string ls = item.Arguments != null && item.Arguments.TryGetValue("learnSkillCommand", out object? ler ) ? ler?.ToString() ?? "" : "";
				var toolResult1 = LearnSkill(ls);
				var toolMessage1 = new ChatMessage(ChatRole.Tool, [new FunctionResultContent(item.CallId, toolResult1)]);
				messages.Add(toolMessage1);
				continue;
			}
			else if (item.Name == "get_skills")
			{

				continue;
			}

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
			messages.Add(toolMessage);

			if (imageContent != null)
			{
				var userImageMessage = new ChatMessage(ChatRole.User, [
					new TextContent("[System] Here is the screenshot you just took. Describe what you see in the pictures."),
					imageContent
				]);
				messages.Add(userImageMessage);
			}
		}
	}
	else
	{
		messages.Add(new ChatMessage(ChatRole.Assistant, fullText));
		break;
	}
}
*/


