using System;

namespace LlmComms.Providers.Ollama;

/// <summary>
/// Configuration options for the Ollama provider.
/// </summary>
public sealed class OllamaProviderOptions
{
    private const string DefaultBaseUrl = "http://localhost:11434";

    /// <summary>
    /// Initializes a new instance of the <see cref="OllamaProviderOptions"/> class.
    /// </summary>
    public OllamaProviderOptions()
    {
        BaseUrl = new Uri(DefaultBaseUrl, UriKind.Absolute);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="OllamaProviderOptions"/> class with the specified base URL.
    /// </summary>
    /// <param name="baseUrl">The root Ollama server URL (e.g., http://localhost:11434).</param>
    public OllamaProviderOptions(string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new ArgumentException("Base URL must be provided.", nameof(baseUrl));

        BaseUrl = new Uri(baseUrl, UriKind.Absolute);
    }

    /// <summary>
    /// Gets or sets the root Ollama server URL.
    /// </summary>
    public Uri BaseUrl { get; set; }
}
