using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SoftcurseMediaLabAI
{
    public sealed class GenerativeApiException : Exception
    {
        public HttpStatusCode? StatusCode { get; }
        public GenerativeApiException(string message, HttpStatusCode? statusCode = null, Exception? inner = null)
            : base(message, inner) => StatusCode = statusCode;
    }

    public static class GenerativeApiClient
    {
        public const long MaxResponseBytes = 128L * 1024 * 1024;
        private static readonly HttpClient Client = new() { Timeout = Timeout.InfiniteTimeSpan };

        public static Uri BuildUri(string baseUrl, string apiPath)
        {
            if (!AppSettings.IsApiEndpointSafe(baseUrl, out string reason))
                throw new GenerativeApiException($"Invalid API endpoint: {reason}");
            if (baseUrl.Contains(apiPath, StringComparison.OrdinalIgnoreCase))
                return new Uri(baseUrl, UriKind.Absolute);
            return new Uri(new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/"), apiPath);
        }

        public static bool IsRemoteEndpoint(string endpoint) =>
            Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri) && !uri.IsLoopback;

        public static async Task<JsonDocument> PostJsonAsync(
            string baseUrl,
            string apiPath,
            object payload,
            CancellationToken cancellationToken,
            TimeSpan? timeout = null)
        {
            string json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri(baseUrl, apiPath))
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            return await SendForJsonAsync(request, cancellationToken, timeout ?? TimeSpan.FromMinutes(5));
        }

        public static async Task<string> TestConnectionAsync(string baseUrl, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(baseUrl, "sdapi/v1/options"));
            using JsonDocument response = await SendForJsonAsync(request, cancellationToken, TimeSpan.FromSeconds(10));
            return response.RootElement.ValueKind == JsonValueKind.Object
                ? "Compatible API responded successfully."
                : "API responded, but returned an unexpected payload.";
        }

        private static async Task<JsonDocument> SendForJsonAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken,
            TimeSpan timeout)
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            try
            {
                using HttpResponseMessage response = await Client.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token);
                long limit = response.IsSuccessStatusCode ? MaxResponseBytes : 64 * 1024;
                string body = await ReadLimitedTextAsync(response.Content, limit, timeoutSource.Token);
                if (!response.IsSuccessStatusCode)
                    throw new GenerativeApiException(
                        $"API returned {(int)response.StatusCode} {response.ReasonPhrase}: {SummarizeError(body)}",
                        response.StatusCode);
                try
                {
                    return JsonDocument.Parse(body);
                }
                catch (JsonException ex)
                {
                    throw new GenerativeApiException("API returned invalid JSON.", response.StatusCode, ex);
                }
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new GenerativeApiException($"API request timed out after {timeout.TotalSeconds:0} seconds.", null, ex);
            }
            catch (HttpRequestException ex)
            {
                throw new GenerativeApiException($"Could not connect to the API: {ex.Message}", null, ex);
            }
        }

        private static async Task<string> ReadLimitedTextAsync(HttpContent content, long maxBytes, CancellationToken token)
        {
            if (content.Headers.ContentLength > maxBytes)
                throw new GenerativeApiException($"API response is too large. Limit: {maxBytes / 1024 / 1024} MB.");
            await using Stream input = await content.ReadAsStreamAsync(token);
            using var output = new MemoryStream();
            byte[] buffer = new byte[81920];
            while (true)
            {
                int read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), token);
                if (read == 0) break;
                if (output.Length + read > maxBytes)
                    throw new GenerativeApiException($"API response exceeded the {maxBytes / 1024 / 1024} MB limit.");
                output.Write(buffer, 0, read);
            }
            return Encoding.UTF8.GetString(output.GetBuffer(), 0, checked((int)output.Length));
        }

        private static string SummarizeError(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return "No error details were provided.";
            string singleLine = body.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return singleLine.Length <= 500 ? singleLine : singleLine[..500] + "…";
        }
    }
}
