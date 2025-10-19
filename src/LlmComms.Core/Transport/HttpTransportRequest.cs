using System.Collections.Generic;
using LlmComms.Abstractions.Ports;

namespace LlmComms.Core.Transport;

/// <summary>
/// Simple DTO describing the essentials of an HTTP request for <see cref="ITransport"/> interactions.
/// </summary>
public sealed class HttpTransportRequest
{
    /// <summary>
    /// Gets or sets the absolute URL for the request.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the HTTP method (defaults to <c>POST</c>).
    /// </summary>
    public string Method { get; set; } = "POST";

    /// <summary>
    /// Gets or sets the request headers.
    /// </summary>
    public IDictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();

    /// <summary>
    /// Gets or sets the request body payload.
    /// </summary>
    public string Body { get; set; } = string.Empty;
}
