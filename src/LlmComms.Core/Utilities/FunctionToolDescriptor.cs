using System;
using System.Collections.Generic;
using LlmComms.Abstractions.Contracts;

namespace LlmComms.Core.Utilities;

/// <summary>
/// Lightweight representation of a tool/function definition shared across providers.
/// </summary>
public sealed class FunctionToolDescriptor
{
    public FunctionToolDescriptor(string name, string description, IReadOnlyDictionary<string, object> parameters)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Description = description ?? string.Empty;
        Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
    }

    /// <summary>
    /// Gets the tool name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the tool description.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Gets the parameters JSON schema payload.
    /// </summary>
    public IReadOnlyDictionary<string, object> Parameters { get; }
}

/// <summary>
/// Factory helpers for turning <see cref="ToolCollection"/> into reusable descriptors.
/// </summary>
public static class FunctionToolDescriptorFactory
{
    /// <summary>
    /// Extracts a list of <see cref="FunctionToolDescriptor"/> from the provided collection.
    /// </summary>
    /// <param name="toolCollection">Tool definitions supplied in a request.</param>
    /// <returns>An immutable list of descriptors (possibly empty).</returns>
    public static IReadOnlyList<FunctionToolDescriptor> CreateDescriptors(ToolCollection? toolCollection)
    {
        if (toolCollection == null || toolCollection.Tools.Count == 0)
            return Array.Empty<FunctionToolDescriptor>();

        var descriptors = new List<FunctionToolDescriptor>(toolCollection.Tools.Count);

        foreach (var tool in toolCollection.Tools)
        {
            descriptors.Add(new FunctionToolDescriptor(tool.Name, tool.Description, tool.JsonSchema));
        }

        return descriptors;
    }
}
