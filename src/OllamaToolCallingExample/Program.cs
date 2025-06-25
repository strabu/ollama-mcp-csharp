using Microsoft.Extensions.AI;
using OllamaSharp;


ChatOptions chatOptions = new()
{
	Tools = [
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
		"Get the current time")
	]
};

string modelId = "qwen3:8b";
var ollama = new OllamaApiClient(new Uri("http://localhost:11434/"), modelId);
var chatClient = new ChatClientBuilder(ollama)
	.UseFunctionInvocation()
	.Build();

await foreach (var update in chatClient.GetStreamingResponseAsync("Which day is it today?", chatOptions))
{
	Console.Write(update.ToString());
}

await foreach (var update in chatClient.GetStreamingResponseAsync("What time is it?", chatOptions))
{
	Console.Write(update.ToString());
}