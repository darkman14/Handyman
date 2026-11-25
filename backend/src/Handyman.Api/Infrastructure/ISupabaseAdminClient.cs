using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace Handyman.Api.Infrastructure;

/// <summary>Supabase Auth admin API (service-role key). Used only by the development seeder; faked in tests.</summary>
public interface ISupabaseAdminClient
{
    /// <summary>Creates a phone user with the phone already confirmed and returns the auth user id.</summary>
    Task<Guid> CreateConfirmedPhoneUserAsync(string phone, CancellationToken ct);
}

public class SupabaseAdminClient(HttpClient http, IOptions<SupabaseOptions> options) : ISupabaseAdminClient
{
    public async Task<Guid> CreateConfirmedPhoneUserAsync(string phone, CancellationToken ct)
    {
        var o = options.Value;
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{o.Url.TrimEnd('/')}/auth/v1/admin/users")
        {
            Content = JsonContent.Create(new { phone, phone_confirm = true }),
        };
        request.Headers.Add("apikey", o.ServiceRoleKey);
        request.Headers.Authorization = new("Bearer", o.ServiceRoleKey);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<CreatedUser>(ct);
        return body!.Id;
    }

    private sealed record CreatedUser(Guid Id);
}
