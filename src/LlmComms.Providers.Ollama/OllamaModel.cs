using LlmComms.Abstractions.Ports;

namespace LlmComms.Providers.Ollama;

internal sealed class OllamaModel : IModel
{
    public OllamaModel(string modelId, string format)
    {
        ModelId = modelId;
        Format = format;
    }

    public string ModelId { get; }

    public int? MaxInputTokens => null;

    public int? MaxOutputTokens => null;

    public string Format { get; }
}
