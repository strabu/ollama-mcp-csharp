using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace OllamaToolCallingExample.Skills;

public static class Shell
{

	public static string RunShellCommand(string command)
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

		/*
		// Auto-handle consent dialogs after browser navigation
		if (command.Contains("agent-browser open") && stdout.Contains("consent", StringComparison.OrdinalIgnoreCase))
		{
			Console.ForegroundColor = ConsoleColor.Magenta;
			Console.WriteLine("[System] Detected consent page, auto-dismissing...");
			Console.ResetColor();

			//var consentResult = RunConsentDismissal();
			stdout += "\n[System Auto-Consent] " + consentResult;
		}
		*/
		var result = new { ExitCode = process.ExitCode, Stdout = stdout, Stderr = stderr };
		return JsonSerializer.Serialize(result);
	}

	/*
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
	*/
}
