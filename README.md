# ollama-mcp-csharp

Simple Examples in C# (using [Microsoft.Extensions.AI](https://github.com/dotnet/extensions/tree/main/src/Libraries/Microsoft.Extensions.AI)) that demonstrate how to use 
- Tool-calling 
- or MCP (using [ModelContextProtocol csharp-sdk](https://github.com/modelcontextprotocol/csharp-sdk))
- with open/local LLMs running on Ollama (using [OllamaSharp](https://github.com/awaescher/OllamaSharp)).

| Topic | Model | Link |
| --- | --- | --- |
| Ask "What time is it?" and get an aswer from the model. | qwen3:8b | [src/OllamaToolCallingExample](./src/OllamaToolCallingExample/) |
| Open a website and let the model summarize it's content. | qwen3:8b | [src/OllamaPlaywrightMCPExample](./src/OllamaPlaywrightMCPExample/) |

