using System;
using LlmComms.Abstractions.Ports;

namespace LlmComms.Core.Transport;

/// <summary>
/// Helper that extracts common response details from arbitrary transport payloads.
/// </summary>
public static class TransportResponseReader
{
    /// <summary>
    /// Reads the status code and response body from the transport response object produced by <see cref="ITransport"/>.
    /// </summary>
    /// <param name="transportResponse">The response object returned by the transport.</param>
    /// <returns>The status code and body string.</returns>
    public static (int StatusCode, string Body) Read(object transportResponse)
    {
        if (transportResponse == null)
            throw new ArgumentNullException(nameof(transportResponse));

        var type = transportResponse.GetType();

        var statusCodeProperty = type.GetProperty("StatusCode")
            ?? throw new InvalidOperationException("Transport response must expose a StatusCode property.");
        var bodyProperty = type.GetProperty("Body")
            ?? throw new InvalidOperationException("Transport response must expose a Body property.");

        if (statusCodeProperty.GetValue(transportResponse) is not int statusCode)
            throw new InvalidOperationException("Transport response StatusCode must be an integer.");

        var body = bodyProperty.GetValue(transportResponse) as string ?? string.Empty;

        return (statusCode, body);
    }
}
