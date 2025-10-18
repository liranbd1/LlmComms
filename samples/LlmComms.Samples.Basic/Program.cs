using System.Linq;
using LlmComms.Abstractions.Contracts;
using LlmComms.Core.Client;
using LlmComms.Providers.Ollama;

var provider = new OllamaProvider(new OllamaProviderOptions("http://localhost:11434"));

var client = new LlmClientBuilder()
    .UseProvider(provider)
    .UseModel("qwen3:4b")
    .Build();

var request = new Request(new List<Message>
{
    new(MessageRole.System, "You are a concise assistant."),
    new(MessageRole.User, "Provide a one-sentence fun fact about space.")
});

Console.WriteLine("Sending request to Ollama (model: qwen3:4b)...\n");

try
{
    var response = await client.SendAsync(request, CancellationToken.None);
    Console.WriteLine($"Ollama response:\n{response.Output.Content}\n");

    if (response.Reasoning is { Segments.Count: > 0 })
    {
        Console.WriteLine("Reasoning trace:");
        foreach (var segment in response.Reasoning.Segments)
        {
            Console.WriteLine(segment.Text);
        }
        Console.WriteLine();
    }
}
catch (Exception ex)
{
    Console.WriteLine("Failed to communicate with Ollama.");
    Console.WriteLine(ex);
}
