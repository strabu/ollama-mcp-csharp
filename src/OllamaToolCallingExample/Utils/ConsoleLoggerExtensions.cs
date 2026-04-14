using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Shared.Diagnostics;
using Microsoft.Extensions.AI;

namespace OllamaToolCallingExample.Skills;

public static class ConsoleLoggerExtensions
{	
	public static ChatClientBuilder UseConsoleLogger(
		this ChatClientBuilder builder,
		ILoggerFactory? loggerFactory = null,
		Action<ConsoleLogger>? configure = null)
	{
		return builder.Use((innerClient, services) =>
		{
			loggerFactory ??= services.GetService<ILoggerFactory>();
			
			var chatClient = new ConsoleLogger(innerClient, services);
			configure?.Invoke(chatClient);
			return chatClient;
		});
	}
}


public class ConsoleLogger : DelegatingChatClient
{
	public ConsoleLogger(IChatClient innerClient, IServiceProvider? functionInvocationServices = null) : base(innerClient)
	{
	}

	public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
	{
		return base.GetResponseAsync(messages, options, cancellationToken);
	}

	public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
	{
		//return base.GetStreamingResponseAsync(messages, options, cancellationToken);
		await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
		{
			foreach (var content in update.Contents)
			{				
				if (content is TextContent text)
				{
					Console.ForegroundColor = ConsoleColor.Gray;
					Console.Write(text.Text);				
				}
				else if (content is FunctionCallContent fcc)
				{
					Console.ForegroundColor = ConsoleColor.DarkCyan;
					string args = string.Join(',', fcc.Arguments.Select(a => a.Key + ": "+a.Value));
					string func = $"{fcc.Name}({args})";
					Console.WriteLine();
					Console.Write(func);
					Console.WriteLine();
				}
				else if (content is FunctionResultContent frc)
				{
					Console.ForegroundColor = ConsoleColor.Cyan;
					Console.Write(frc.Result);
				}
				else if (content is TextReasoningContent reasoning)
				{
					Console.ForegroundColor = ConsoleColor.Yellow;
					Console.Write(reasoning.Text);
					Console.ResetColor();
				}
			}
			yield return update;
		}
	}

}