using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace OllamaToolCallingExample.Skills;

// Pattern: https://github.com/dotnet/extensions/blob/main/src/Libraries/Microsoft.Extensions.AI/ChatCompletion/FunctionInvokingChatClient.cs
public class SkillCallingChatClient : DelegatingChatClient
{
	private const int MaximumIterationsPerRequest = 40;

	/// <summary>
	/// When set, <c>run_shell</c> tool calls are executed via this delegate. The caller's message list is never mutated.
	/// </summary>
	public Func<string, string>? ShellCommand { get; set; }

	/// <summary>Optional override for <c>get_skills</c> text; defaults to a short built-in catalog.</summary>
	public Func<string>? SkillsCatalog { get; set; }

	public SkillCallingChatClient(IChatClient innerClient, IServiceProvider? functionInvocationServices = null) : base(innerClient)
	{
	}

	public override async Task<ChatResponse> GetResponseAsync(
		IEnumerable<ChatMessage> messages,
		ChatOptions? options = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(messages);

		List<ChatMessage> conversation = [.. messages];
		ChatOptions? opts = options;
		List<ChatMessage> allMessages = [];
		UsageDetails? totalUsage = null;

		for (int iteration = 0; iteration < MaximumIterationsPerRequest; iteration++)
		{
			var response = await base.GetResponseAsync(conversation, opts, cancellationToken).ConfigureAwait(false);
			if (response is null)
				throw new InvalidOperationException($"The inner {nameof(IChatClient)} returned a null {nameof(ChatResponse)}.");

			allMessages.AddRange(response.Messages);

			if (response.Usage is not null)
			{
				if (totalUsage is not null)
					totalUsage.Add(response.Usage);
				else
					totalUsage = response.Usage;
			}

			List<FunctionCallContent>? functionCallContents = null;
			foreach (var m in response.Messages)
				_ = CopyFunctionCalls(m.Contents, ref functionCallContents);

			if (functionCallContents is not { Count: > 0 })
			{
				response.Messages = allMessages;
				response.Usage = totalUsage;
				return response;
			}

			conversation.AddRange(response.Messages);
			var added = await InvokeToolCallsAsync(functionCallContents, cancellationToken).ConfigureAwait(false);
			conversation.AddRange(added);
			allMessages.AddRange(added);

			opts = UpdateOptionsForNextIteration(opts, response.ConversationId);
		}

		return new ChatResponse(allMessages) { Usage = totalUsage };
	}

	public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
		IEnumerable<ChatMessage> messages,
		ChatOptions? options = null,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(messages);

		List<ChatMessage> conversation = [.. messages];
		ChatOptions? opts = options;
		var toolMessageId = Guid.NewGuid().ToString("N");

		for (int iteration = 0; iteration < MaximumIterationsPerRequest; iteration++)
		{
			List<ChatResponseUpdate> updates = [];
			List<FunctionCallContent>? functionCallContents = null;

			await foreach (var update in base.GetStreamingResponseAsync(conversation, opts, cancellationToken).ConfigureAwait(false))
			{
				updates.Add(update);
				_ = CopyFunctionCalls(update.Contents, ref functionCallContents);
				yield return update;
			}

			var response = updates.ToChatResponse();

			if (functionCallContents is not { Count: > 0 })
				yield break;

			conversation.AddRange(response.Messages);

			var added = await InvokeToolCallsAsync(functionCallContents, cancellationToken).ConfigureAwait(false);
			foreach (var message in added)
			{
				yield return ConvertToolResultMessageToUpdate(message, response.ConversationId, toolMessageId);
			}

			conversation.AddRange(added);
			opts = UpdateOptionsForNextIteration(opts, response.ConversationId);
		}
	}

	private async Task<List<ChatMessage>> InvokeToolCallsAsync(
		List<FunctionCallContent> functionCallContents,
		CancellationToken cancellationToken)
	{
		var resultContents = new List<FunctionResultContent>();
		var extraMessages = new List<ChatMessage>();

		foreach (var item in functionCallContents)
		{
			if (item.InformationalOnly)
				continue;

			cancellationToken.ThrowIfCancellationRequested();

			string toolResult;
			switch (item.Name)
			{
				case "learn_skill":
				{
					string cmd = item.Arguments is not null && item.Arguments.TryGetValue("learnSkillCommand", out object? ler)
						? ler?.ToString() ?? ""
						: "";
					toolResult = LearnSkill(cmd);
					break;
				}
				case "get_skills":
					toolResult = SkillsCatalog?.Invoke() ?? DefaultSkillsCatalog;
					break;
				case "run_shell":
				{
					string command = item.Arguments is not null && item.Arguments.TryGetValue("command", out object? cmdObj)
						? cmdObj?.ToString() ?? ""
						: "";
					if (ShellCommand is null)
					{
						toolResult = JsonSerializer.Serialize(new
						{
							error = "Shell execution is not configured. Set SkillCallingChatClient.ShellCommand when building the client."
						});
					}
					else
					{
						toolResult = ShellCommand(command);
						await AppendScreenshotFollowUpAsync(command, extraMessages, cancellationToken).ConfigureAwait(false);
					}

					break;
				}
				default:
					toolResult = JsonSerializer.Serialize(new { error = $"Unhandled tool: {item.Name}" });
					break;
			}

			resultContents.Add(new FunctionResultContent(item.CallId, toolResult));
			item.InformationalOnly = true;
		}

		if (resultContents.Count > 0)
		{
			List<AIContent> toolContents = [.. resultContents];
			extraMessages.Insert(0, new ChatMessage(ChatRole.Tool, toolContents));
		}

		return extraMessages;
	}

	private static async Task AppendScreenshotFollowUpAsync(
		string command,
		List<ChatMessage> extraMessages,
		CancellationToken cancellationToken)
	{
		if (!command.Contains("screenshot", StringComparison.Ordinal))
			return;

		var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		for (int i = 0; i < parts.Length; i++)
		{
			if (parts[i] != "screenshot" || i + 1 >= parts.Length)
				continue;

			string fileName = parts[i + 1];
			if (!File.Exists(fileName))
				continue;

			byte[] imageBytes = await File.ReadAllBytesAsync(fileName, cancellationToken).ConfigureAwait(false);
			var imageContent = new DataContent(imageBytes, "image/png");
			Console.WriteLine($"\n[System] Attached image: {fileName}");
			extraMessages.Add(new ChatMessage(ChatRole.User, [
				new TextContent("[System] Here is the screenshot you just took. Describe what you see in the pictures."),
				imageContent
			]));
			break;
		}
	}

	private static bool CopyFunctionCalls(IList<AIContent> content, ref List<FunctionCallContent>? functionCalls)
	{
		bool any = false;
		for (int i = 0; i < content.Count; i++)
		{
			if (content[i] is FunctionCallContent functionCall && !functionCall.InformationalOnly)
			{
				(functionCalls ??= []).Add(functionCall);
				any = true;
			}
		}

		return any;
	}

	private static ChatOptions? UpdateOptionsForNextIteration(ChatOptions? options, string? conversationId)
	{
		if (conversationId is null)
			return options;

		if (options is null)
			return new ChatOptions { ConversationId = conversationId };

		if (options.ConversationId == conversationId && options.ToolMode is not RequiredChatToolMode)
			return options;

		var clone = options.Clone();
		clone.ConversationId = conversationId;
		if (clone.ToolMode is RequiredChatToolMode)
			clone.ToolMode = null;
		return clone;
	}

	private static ChatResponseUpdate ConvertToolResultMessageToUpdate(ChatMessage message, string? conversationId, string? messageId) =>
		new()
		{
			AdditionalProperties = message.AdditionalProperties,
			AuthorName = message.AuthorName,
			ConversationId = conversationId,
			CreatedAt = DateTimeOffset.UtcNow,
			Contents = message.Contents,
			RawRepresentation = message.RawRepresentation,
			ResponseId = messageId,
			MessageId = messageId,
			Role = message.Role,
		};

	private const string DefaultSkillsCatalog =
		"*browser*: use the local webbrowser\r\n*file*: read or write files";

	private static string LearnSkill(string command)
	{
		_ = command;
		return @"When browsing the web use run_shell and then:
1. Use 'agent-browser open <url>' to navigate. Cookie/consent dialogs are handled automatically.
2a. Run 'agent-browser screenshot [path]' to take a screenshot.
2b. Run 'agent-browser snapshot' to see the page structure and find element references (e.g., [ref=e12]).
3. Use 'agent-browser click @ref' to click elements by their reference ID from the snapshot.
4. Use 'agent-browser get text @ref' to extract text from elements.
5. IMPORTANT: After opening a page, always run 'agent-browser snapshot' to inspect the content and find what you need.";
	}
}
