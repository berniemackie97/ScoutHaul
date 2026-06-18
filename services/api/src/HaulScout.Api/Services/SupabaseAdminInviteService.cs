using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;

namespace HaulScout.Api.Services;

public sealed class SupabaseAdminInviteService(HttpClient httpClient, IConfiguration configuration, ILogger<SupabaseAdminInviteService> logger)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(configuration["Authentication:Supabase:Url"])
        && !string.IsNullOrWhiteSpace(configuration["Authentication:Supabase:ServiceRoleKey"]);

    public async Task<bool> SendInviteAsync(
        string email,
        string displayName,
        Guid organizationId,
        string organizationName,
        CancellationToken cancellationToken)
    {
        string? projectUrl = configuration["Authentication:Supabase:Url"]?.Trim().TrimEnd('/');
        string? serviceRoleKey = configuration["Authentication:Supabase:ServiceRoleKey"];

        if (string.IsNullOrWhiteSpace(projectUrl) || string.IsNullOrWhiteSpace(serviceRoleKey))
        {
            logger.LogInformation("Supabase service role key is not configured; skipping invite email for {Email}.", email);
            return false;
        }

        string url = $"{projectUrl}/auth/v1/invite";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("apikey", serviceRoleKey);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", serviceRoleKey);
        request.Content = JsonContent.Create(new
        {
            email,
            data = new
            {
                name = displayName,
                organization_id = organizationId,
                organization_name = organizationName
            },
            redirect_to = configuration["Authentication:Supabase:InviteRedirectUrl"]
        });

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        logger.LogWarning("Supabase invite failed for {Email}. Status {StatusCode}. Body: {Body}", email, response.StatusCode, body);
        return false;
    }
}
