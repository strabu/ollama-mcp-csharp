using Microsoft.Extensions.AI;
using OllamaSharp;
using OllamaSharp.Models;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;

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

	return $"ExitCode: {process.ExitCode}\nStdout:\n{stdout}\nStderr:\n{stderr}";
}

/*
static string GetOsShellContext()
{
	if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "Windows (cmd.exe: dir, type, cd, etc.)";
	if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return "Linux (bash: ls, cat, pwd, etc.)";
	if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return "macOS (bash: ls, cat, pwd, etc.)";
	return RuntimeInformation.OSDescription;
}
*/

ChatOptions chatOptions = new()
{
	Tools = [
		/*
		AIFunctionFactory.Create((string location, string unit) =>
		{			
			return DateTime.Now.DayOfWeek.ToString();
		},
		"get_current_day",
		"Get the current day as a string"),

		AIFunctionFactory.Create((string location, string unit) =>
		{
			return DateTime.Now.TimeOfDay.ToString();
		},
		"get_current_time",
		"Get the current time"),
		*/
		AIFunctionFactory.Create((string command) => RunShellCommand(command),
			"run_shell",
			//$"You are on {GetOsShellContext()}. Execute a shell command (e.g. agent-browser open example.com, ls/dir, git status, pwd/cd, cat/type file.txt) and return exit code, stdout and stderr. Use for any shell command.")
			$"Execute a shell command (e.g. agent-browser open example.com, agent-browser snapshot, agent-browser click @e2, ls/dir, git status, pwd/cd, cat/type file.txt) and return exit code, stdout and stderr. Use for any shell command.")
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
	.UseFunctionInvocation()
	.Build();

/*
await foreach (var update in chatClient
.GetStreamingResponseAsync("run_shell command: 'agent-browser' to open https://derstandard.at and tell me the 3 top international news headlines from today", chatOptions))
{ Console.Write(update.ToString()); }
*/

List<ChatMessage> messages =
[
	//new(ChatRole.System, "You are a helpful assistant with access to tools. ALWAYS use the run_shell tool to execute commands when the user asks you to interact with the file system, run programs, or perform any task that requires shell access. Never refuse or say you cannot do something if a tool is available to accomplish it."),
	/*
	new(ChatRole.System, @"You are a helpful assistant with access to tools. 
ALWAYS use the run_shell tool to execute commands when the user asks you to interact with the file system, run programs, or perform any task that requires shell access.
Be creative and use the shell to get information for answers.
Use lynx to browse the web."),
	*/
	new(ChatRole.System, @"You are a helpful assistant with access to tools. 
ALWAYS use the run_shell tool to execute commands when the user asks you to interact with the file system, run programs, browse the web or perform any task that requires shell access.
Be creative and use the shell to get information for answers. When browsing the web, watch for consent-dialogs and accept them by clicking the Acceppt-Buttons.
With run_shell call 'agent-browser' to browse the web:
	agent-browser open example.com
	agent-browser snapshot                    # Get accessibility tree with refs
	agent-browser click @e2                   # Click by ref from snapshot to get more details
	agent-browser fill @e3 test@example.com   # Fill by ref
	agent-browser get text @e1                # Get text by ref
	agent-browser screenshot page.png
	agent-browser close"),


//Use lynx to browse the web."),
	
	//new(ChatRole.User, "List files in the current directory")
	//new(ChatRole.User, "What time is it?")
	
	//new(ChatRole.User, "What are the tech-news headlines in derstandard.at? (accept/click consent if necessary)")

	new(ChatRole.User, "browse to derstandard.at, execute agent-browser screenshot page.png and tell me what you see on page.png")


	//new(ChatRole.User, "Get the News page from www.orf.at and make me a summary about Iran.")
	//new(ChatRole.User, "What's in the latest 13F filing from Berkshire Hathaway?")
];

await foreach (var update in chatClient.GetStreamingResponseAsync(messages, chatOptions))
{ 
	if (update != null)
		Console.Write(update.ToString()); 
}

/*
await foreach (var update in chatClient.GetStreamingResponseAsync("Which day is it today? We are in innsbruck", chatOptions))
{
	Console.Write(update.ToString());
}

await foreach (var update in chatClient.GetStreamingResponseAsync("What time is it in vienna?", chatOptions))
{
	Console.Write(update.ToString());
}
*/



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