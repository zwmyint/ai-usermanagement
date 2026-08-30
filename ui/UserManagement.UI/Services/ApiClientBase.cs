using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using UserManagement.UI.Contracts;

namespace UserManagement.UI.Services;

/// <summary>Shared JSON send/parse logic for API clients; unwraps the ApiResponse envelope and throws ApiException on failure.</summary>
public abstract class ApiClientBase
{
    protected static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected readonly HttpClient Http;

    protected ApiClientBase(HttpClient http) => Http = http;

    protected async Task<T> SendAsync<T>(HttpMethod method, string url, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null)
            request.Content = JsonContent.Create(body, options: JsonOptions);

        using var response = await Http.SendAsync(request, ct);
        var envelope = await ReadEnvelopeAsync<T>(response, ct);

        if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success)
        {
            throw new ApiException(
                envelope?.Message ?? $"Request failed with status {(int)response.StatusCode}.",
                (int)response.StatusCode,
                envelope?.Errors);
        }

        return envelope.Data!;
    }

    protected async Task SendAsync(HttpMethod method, string url, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null)
            request.Content = JsonContent.Create(body, options: JsonOptions);

        using var response = await Http.SendAsync(request, ct);
        var envelope = await ReadEnvelopeAsync<object>(response, ct);

        if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success)
        {
            throw new ApiException(
                envelope?.Message ?? $"Request failed with status {(int)response.StatusCode}.",
                (int)response.StatusCode,
                envelope?.Errors);
        }
    }

    protected async Task<T> SendMultipartAsync<T>(HttpMethod method, string url, IFormFile file, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url);
        using var form = new MultipartFormDataContent();
        await using var stream = file.OpenReadStream();
        using var content = new StreamContent(stream);
        if (!string.IsNullOrWhiteSpace(file.ContentType))
            content.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
        form.Add(content, "profilePicture", file.FileName);
        request.Content = form;

        using var response = await Http.SendAsync(request, ct);
        var envelope = await ReadEnvelopeAsync<T>(response, ct);
        if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success)
        {
            throw new ApiException(
                envelope?.Message ?? $"Request failed with status {(int)response.StatusCode}.",
                (int)response.StatusCode,
                envelope?.Errors);
        }

        return envelope.Data!;
    }

    private static async Task<ApiResponse<T>?> ReadEnvelopeAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ApiResponse<T>>(JsonOptions, ct);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
