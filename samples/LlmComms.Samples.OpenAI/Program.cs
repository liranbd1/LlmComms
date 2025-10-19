using LlmComms.Abstractions.Contracts;
using LlmComms.Core.Client;
using LlmComms.Providers.OpenAI;

var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");

if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.WriteLine("OPENAI_API_KEY environment variable is not set. Please provide an API key before running the sample.");
    return;
}

var provider = new OpenAIProvider(new OpenAIProviderOptions
{
    ApiKey = apiKey,
    DefaultModelId = "gpt-4o-mini"
});

var client = new LlmClientBuilder()
    .UseProvider(provider)
    .UseModel("gpt-4o-mini")
    .Build();

var request = new Request(new List<Message>
{
    new(MessageRole.System, "You are a concise assistant."),
    new(MessageRole.User, "Share a short productivity tip for developers.")
});

Console.WriteLine("Sending request to OpenAI (model: gpt-4o-mini)...\n");

try
{
    var response = await client.SendAsync(request, CancellationToken.None);
    Console.WriteLine($"OpenAI response:\n{response.Output.Content}\n");
}
catch (Exception ex)
{
    Console.WriteLine("Failed to communicate with OpenAI.");
    Console.WriteLine(ex);
}
