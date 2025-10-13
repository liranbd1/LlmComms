using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LlmComms.Abstractions.Ports;

namespace LlmComms.Core.Transport;

/// <summary>
/// Default HTTP transport implementation using HttpClient.
/// </summary>
public sealed class HttpClientTransport : ITransport
{
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="HttpClientTransport"/> class with a default HttpClient.
    /// </summary>
    public HttpClientTransport()
        : this(new HttpClient())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HttpClientTransport"/> class with a provided HttpClient.
    /// </summary>
    /// <param name="httpClient">The HttpClient instance to use.</param>
    public HttpClientTransport(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    /// <summary>
    /// Sends an HTTP request and returns the response.
    /// </summary>
    /// <param name="request">
    /// The request object. Expected to be a dictionary or object with properties:
    /// - Url (string): The request URL
    /// - Method (string): HTTP method (GET, POST, etc.)
    /// - Headers (IDictionary&lt;string, string&gt;): Request headers
    /// - Body (string): Request body (optional)
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A response object with properties:
    /// - StatusCode (int): HTTP status code
    /// - Headers (IDictionary&lt;string, IEnumerable&lt;string&gt;&gt;): Response headers
    /// - Body (string): Response body
    /// </returns>
    public async Task<object> SendAsync(object request, CancellationToken cancellationToken)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        var httpRequest = BuildHttpRequestMessage(request);

        try
        {
            var httpResponse = await _httpClient.SendAsync(httpRequest, cancellationToken)
                .ConfigureAwait(false);

            var responseBody = await httpResponse.Content.ReadAsStringAsync()
#if NET5_0_OR_GREATER
                .ConfigureAwait(false);
#else
                ;
#endif

            return new
            {
                StatusCode = (int)httpResponse.StatusCode,
                Headers = httpResponse.Headers.ToDictionary(
                    h => h.Key,
                    h => (IEnumerable<string>)h.Value
                ),
                Body = responseBody
            };
        }
        finally
        {
            httpRequest?.Dispose();
        }
    }

    private HttpRequestMessage BuildHttpRequestMessage(object request)
    {
        // Use reflection or dynamic to extract properties from request object
        var requestType = request.GetType();

        var urlProperty = requestType.GetProperty("Url");
        var methodProperty = requestType.GetProperty("Method");
        var headersProperty = requestType.GetProperty("Headers");
        var bodyProperty = requestType.GetProperty("Body");

        if (urlProperty == null || methodProperty == null)
        {
            throw new ArgumentException(
                "Request object must have 'Url' and 'Method' properties.",
                nameof(request)
            );
        }

        var url = urlProperty.GetValue(request) as string
            ?? throw new ArgumentException("Url property must be a string.", nameof(request));

        var methodString = methodProperty.GetValue(request) as string
            ?? throw new ArgumentException("Method property must be a string.", nameof(request));

        var httpMethod = new HttpMethod(methodString);
        var httpRequest = new HttpRequestMessage(httpMethod, url);

        // Add headers if present
        if (headersProperty != null)
        {
            var headers = headersProperty.GetValue(request) as IDictionary<string, string>;
            if (headers != null)
            {
                foreach (var header in headers)
                {
                    httpRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }
        }

        // Add body if present (for POST/PUT/PATCH)
        if (bodyProperty != null)
        {
            var body = bodyProperty.GetValue(request) as string;
            if (!string.IsNullOrEmpty(body))
            {
                httpRequest.Content = new StringContent(
                    body,
                    Encoding.UTF8,
                    "application/json"
                );
            }
        }

        return httpRequest;
    }
}