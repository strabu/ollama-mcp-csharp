using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.AI;
using OllamaSharp;

static string RunShellCommand(string command)
{
	if (string.IsNullOrWhiteSpace(command))
		return "Error: command cannot be empty.";	

	var (fileName, arguments) = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
		? ("cmd.exe", $"/c \"{command.Replace("\"", "\\\"")}\"")
		: ("/bin/bash", $"-c \"{command.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"");

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
	var stderr = process.StandardError.ReadToEnd();
	process.WaitForExit(TimeSpan.FromSeconds(60));

	return $"ExitCode: {process.ExitCode}\nStdout:\n{stdout}\nStderr:\n{stderr}";
}

static string GetOsShellContext()
{
	if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "Windows (cmd.exe: dir, type, cd, etc.)";
	if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return "Linux (bash: ls, cat, pwd, etc.)";
	if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return "macOS (bash: ls, cat, pwd, etc.)";
	return RuntimeInformation.OSDescription;
}

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
			$"You are on {GetOsShellContext()}. Execute a shell command (e.g. agent-browser open example.com, ls/dir, git status, pwd/cd, cat/type file.txt) and return exit code, stdout and stderr. Use for any shell command.")
	]
};

//string modelId = "qwen3:8b";
string modelId = "ministral-3:8b";
var ollama = new OllamaApiClient(new Uri("http://localhost:11434/"), modelId);
var chatClient = new ChatClientBuilder(ollama)
	.UseFunctionInvocation()
	.Build();

/*
await foreach (var update in chatClient
.GetStreamingResponseAsync("run_shell command: 'agent-browser' to open https://derstandard.at and tell me the 3 top international news headlines from today", chatOptions))
{ Console.Write(update.ToString()); }
*/

List<ChatMessage> messages =
[
	new(ChatRole.System, "You are a helpful assistant with access to tools. ALWAYS use the run_shell tool to execute commands when the user asks you to interact with the file system, run programs, or perform any task that requires shell access. Never refuse or say you cannot do something if a tool is available to accomplish it."),
	new(ChatRole.User, "List files in the current directory")
];

await foreach (var update in chatClient.GetStreamingResponseAsync(messages, chatOptions))
{ Console.Write(update.ToString()); }

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