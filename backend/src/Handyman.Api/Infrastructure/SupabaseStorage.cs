using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace Handyman.Api.Infrastructure;

/// <summary>Supabase Storage through its REST API, using the service-role key (server side only).</summary>
public class SupabaseStorage(HttpClient http, IOptions<SupabaseOptions> options) : IBlobStorage
{
    private string Base => options.Value.Url.TrimEnd('/') + "/storage/v1";

    private HttpRequestMessage Request(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.ServiceRoleKey);
        return request;
    }

    public async Task<string> UploadAsync(string bucket, string path, Stream content, string contentType, CancellationToken ct = default)
    {
        using var request = Request(HttpMethod.Post, $"{Base}/object/{bucket}/{path}");
        request.Content = new StreamContent(content);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return path;
    }

    public async Task<string> GetSignedUrlAsync(string bucket, string path, TimeSpan lifetime, CancellationToken ct = default)
    {
        using var request = Request(HttpMethod.Post, $"{Base}/object/sign/{bucket}/{path}");
        request.Content = JsonContent.Create(new { expiresIn = (int)lifetime.TotalSeconds });
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<SignedUrl>(ct);
        return $"{Base}{body!.SignedURL}";
    }

    public async Task DeleteAsync(string bucket, string path, CancellationToken ct = default)
    {
        using var request = Request(HttpMethod.Delete, $"{Base}/object/{bucket}/{path}");
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    private sealed record SignedUrl(string SignedURL);
}
