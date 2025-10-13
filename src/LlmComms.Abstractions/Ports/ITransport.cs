namespace LlmComms.Abstractions.Ports;

/// <summary>
/// Abstracts HTTP communication for testability and customization.
/// </summary>
public interface ITransport
{
    /// <summary>
    /// Sends an HTTP request and returns the response.
    /// </summary>
    /// <param name="request">The request object (transport-specific format).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response object (transport-specific format).</returns>
    Task<object> SendAsync(object request, CancellationToken cancellationToken);
}