using Microsoft.Extensions.AI;
using OllamaSharp;
using OllamaSharp.Models;
using OllamaToolCallingExample;
using OllamaToolCallingExample.Skills;

var _skills = new Skills();

ChatOptions chatOptions = new()
{
	Tools = [		
		AIFunctionFactory.Create((string learnSkillCommand) => _skills.LearnSkill(learnSkillCommand),
			"learn_skill", $"Returns the details for a skill and how to use it"),
		
		AIFunctionFactory.Create((string command) => Shell.RunShellCommand(command),
			"run_shell", $"Execute a shell command and return exit code, stdout and stderr. Use for any shell command."),

		AIFunctionFactory.Create((string imageFilePath) => _skills.LookAtImageFile(imageFilePath),
			"look_at_image_file", $"Returns the data of an Image File"),

		AIFunctionFactory.Create((string filePath, string content) => _skills.WriteFile(filePath, content),
			"write_file", "Writes UTF-8 text to a file. Creates parent directories if needed. Overwrites an existing file."),
	]
};

//https://agentskills.io/client-implementation/adding-skills-support#frontmatter-extraction
//https://github.com/dotnet/extensions/tree/main/src/Libraries/Microsoft.Extensions.AI.Abstractions

//string modelId = "ministral-3:8b"; //good but not as good as gemma4
//string modelId = "qwen3:8b";
//string modelId = "qwen3.5:9b";
//string modelId = "qwen3.6:latest"; //very good
string modelId = "gemma4:e4b"; //the best

var ollamaUri = new Uri("http://localhost:11434/");
using var httpClient = new HttpClient(new DetailedHttpFailureHandler()) { BaseAddress = ollamaUri };
var ollama = new OllamaApiClient(httpClient, modelId);

var chatClient = new ChatClientBuilder(ollama)
	//.ConfigureOptions(c => c.AddOllamaOption(OllamaOption.NumCtx, 16384))
	.ConfigureOptions(c => c.AddOllamaOption(OllamaOption.NumCtx, 32768))
	// .UseFunctionInvocation() // We will handle this manually
	.UseConsoleLogger()
	//.UseSkills(configure: c => c.ShellCommand = RunShellCommand)
	.UseSkills(configure: c => c.Skills = _skills)
	.Build();

List<ChatMessage> messages =
[

	new(ChatRole.System, $@"You are a helpful assistant with access to many tools and skills. 
Skills are **NOT** TOOLS. 
You *must* always call learn_skill(skillName) before you can use a skill. 
These are the skills (NOT tools): {_skills.GetSkills()}"),
	

	//new(ChatRole.System, $@"You are a helpful assistant with access to many tools and skills."),

	//new(ChatRole.User, "List files in the current directory")
	//new(ChatRole.User, "List files in the current directory")
	//new(ChatRole.User, "What time is it?")
new(ChatRole.User, "1) Write a Hello-World program in C# 2) put it into c:\\temp\\hello\\program.cs")
	
	//new(ChatRole.User, "What are the tech-news headlines in derstandard.at? (accept/click consent if necessary)")	
	//new(ChatRole.User, "Browse to derstandard.at? Take a screenshot from the page and tell me what the pictures on the page show.")
	
	//new(ChatRole.User, "Browse to derstandard.at/web ? Take a screenshot from the page and tell me what the pictures on the page show.")

//new(ChatRole.User, "What do you see in: C:\\Users\\Alex\\Pictures\\S1.png ?")


//new(ChatRole.User, "Search yahoo finance for the latest SEC 13f-filing from Berkshire Hathaway.")
	//new(ChatRole.User, "Get the News page from www.orf.at and make me a summary about Iran.")
	//new(ChatRole.User, "What's in the latest 13F filing from Berkshire Hathaway?")
];

//while (true)
{
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
}

