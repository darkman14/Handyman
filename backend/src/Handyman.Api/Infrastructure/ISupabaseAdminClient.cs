using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Handyman.Api.Data.Seed;
using Microsoft.Extensions.Options;

namespace Handyman.Api.Infrastructure;

/// <summary>Supabase Auth admin API (service-role key). Used only by the development seeder; faked in tests.</summary>
public interface ISupabaseAdminClient
{
    /// <summary>Creates a phone user with the phone already confirmed and returns the auth user id.</summary>
    Task<Guid> CreateConfirmedPhoneUserAsync(string phone, CancellationToken ct);

    /// <summary>Returns the id of the auth user with this phone (any format, compared by digits), or null.</summary>
    Task<Guid?> FindUserIdByPhoneAsync(string phone, CancellationToken ct);
}

public class SupabaseAdminClient(HttpClient http, IOptions<SupabaseOptions> options) : ISupabaseAdminClient
{
    private const int PageSize = 200;

    public async Task<Guid> CreateConfirmedPhoneUserAsync(string phone, CancellationToken ct)
    {
        using var request = NewRequest(HttpMethod.Post, "/auth/v1/admin/users");
        request.Content = JsonContent.Create(new { phone, phone_confirm = true });
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<AuthUser>(ct);
        return body!.Id;
    }

    public async Task<Guid?> FindUserIdByPhoneAsync(string phone, CancellationToken ct)
    {
        var wanted = DevUsers.NormalizePhone(phone);
        for (var page = 1; ; page++)
        {
            using var request = NewRequest(HttpMethod.Get, $"/auth/v1/admin/users?page={page}&per_page={PageSize}");
            using var response = await http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<UserList>(ct);
            var users = body?.Users ?? [];
            var match = users.FirstOrDefault(u => !string.IsNullOrEmpty(u.Phone) && DevUsers.NormalizePhone(u.Phone) == wanted);
            if (match is not null)
                return match.Id;
            if (users.Count < PageSize)
                return null;
        }
    }

    private HttpRequestMessage NewRequest(HttpMethod method, string path)
    {
        var o = options.Value;
        var request = new HttpRequestMessage(method, $"{o.Url.TrimEnd('/')}{path}");
        request.Headers.Add("apikey", o.ServiceRoleKey);
        request.Headers.Authorization = new("Bearer", o.ServiceRoleKey);
        return request;
    }

    private sealed record AuthUser(Guid Id, string? Phone = null);

    private sealed record UserList([property: JsonPropertyName("users")] List<AuthUser>? Users);
}
