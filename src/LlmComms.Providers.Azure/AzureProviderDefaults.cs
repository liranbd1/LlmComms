namespace LlmComms.Providers.Azure;

/// <summary>
/// Shared constants used across the Azure provider implementations.
/// </summary>
internal static class AzureProviderDefaults
{
    /// <summary>
    /// The API version used when none is specified for Azure OpenAI REST calls.
    /// </summary>
    public const string DefaultAzureOpenAIApiVersion = "2024-05-01-preview";

    /// <summary>
    /// The default Microsoft Entra scope used when fetching bearer tokens for Azure OpenAI.
    /// </summary>
    public const string DefaultAzureOpenAIScope = "https://cognitiveservices.azure.com/.default";

    /// <summary>
    /// The API version used when none is specified for Azure AI Inference REST calls.
    /// </summary>
    public const string DefaultAzureAIInferenceApiVersion = "2024-05-01-preview";
}
