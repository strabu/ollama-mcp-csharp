using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Shared.Diagnostics;
using Microsoft.Extensions.AI;

namespace OllamaToolCallingExample.Skills;

/// <summary>
/// Provides extension methods for attaching a <see cref="FunctionInvokingChatClient"/> to a chat pipeline.
/// </summary>
public static class SkillCallExtensions
{
	/// <summary>
	/// Enables automatic function call invocation on the chat pipeline.
	/// </summary>
	/// <remarks>This works by adding an instance of <see cref="FunctionInvokingChatClient"/> with default options.</remarks>
	/// <param name="builder">The <see cref="ChatClientBuilder"/> being used to build the chat pipeline.</param>
	/// <param name="loggerFactory">An optional <see cref="ILoggerFactory"/> to use to create a logger for logging function invocations.</param>
	/// <param name="configure">An optional callback that can be used to configure the <see cref="FunctionInvokingChatClient"/> instance.</param>
	/// <returns>The supplied <paramref name="builder"/>.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
	public static ChatClientBuilder UseSkills(
		this ChatClientBuilder builder,
		ILoggerFactory? loggerFactory = null,
		Action<SkillCallingChatClient>? configure = null)
	{
		return builder.Use((innerClient, services) =>
		{
			loggerFactory ??= services.GetService<ILoggerFactory>();

			//var chatClient = new FunctionInvokingChatClient(innerClient, loggerFactory, services);
			var chatClient = new SkillCallingChatClient(innerClient, services);
			configure?.Invoke(chatClient);
			return chatClient;
		});
	}
}