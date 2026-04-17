using Microsoft.Extensions.AI;
using OllamaSharp.Tools;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;

namespace OllamaToolCallingExample.Skills;

// Pattern: https://github.com/dotnet/extensions/blob/main/src/Libraries/Microsoft.Extensions.AI/ChatCompletion/FunctionInvokingChatClient.cs
public class SkillCallingChatClient : DelegatingChatClient
{
	private const int MaximumIterationsPerRequest = 40;
	
	public Skills Skills { get; set; }

	/// <summary>Optional override for <c>get_skills</c> text; defaults to a short built-in catalog.</summary>
	public Func<string>? SkillsCatalog { get; set; }

	//private Skills _skills = new Skills();

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

			string toolResult = null;
			switch (item.Name)
			{
				case "learn_skill":
				{
					string cmd = item.Arguments is not null && item.Arguments.TryGetValue("learnSkillCommand", out object? ler)
						? ler?.ToString() ?? ""
						: "";
					toolResult = Skills.LearnSkill(cmd);
					break;
				}
				case "get_skills":
					toolResult = SkillsCatalog?.Invoke() ?? Skills.GetSkills();
					break;
				case "run_shell":
				{
					string command = item.Arguments is not null && item.Arguments.TryGetValue("command", out object? cmdObj)
						? cmdObj?.ToString() ?? ""
						: "";
					
					toolResult = Shell.RunShellCommand(command);
					//await AppendScreenshotFollowUpAsync(command, toolResult, extraMessages, cancellationToken).ConfigureAwait(false);
					/*var re = await AppendScreenshotFollowUpBinAsync(item.CallId, command, toolResult, extraMessages, cancellationToken).ConfigureAwait(false);
					if (re != null)
						resultContents.Add(re);*/
					break;
				}
				case "look_at_image_file":
				{
					string cmd = item.Arguments is not null && item.Arguments.TryGetValue("imageFilePath", out object? ler)
						? ler?.ToString() ?? ""
						: "";
					var imageResult = Skills.LookAtImageFile(cmd);
					resultContents.Add(new FunctionResultContent(item.CallId, imageResult));
					break;
				}
				default:
					toolResult = JsonSerializer.Serialize(new { error = $"Unhandled tool: {item.Name}" });
					break;
			}

			if (toolResult != null)
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

	/*
	private static async Task<FunctionResultContent> AppendScreenshotFollowUpBinAsync(string callID,
		string command, string toolResult,
		List<ChatMessage> extraMessages,
		CancellationToken cancellationToken)
	{
		if (!command.Contains("screenshot", StringComparison.Ordinal))
			return null;

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
			//Console.WriteLine($"\n[System] Attached image: {fileName}");
			return new FunctionResultContent(callID, imageContent);			
		}
		return null;
	}
	*/
	private static async Task AppendScreenshotFollowUpAsync(
		string command, string toolResult,
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
}
