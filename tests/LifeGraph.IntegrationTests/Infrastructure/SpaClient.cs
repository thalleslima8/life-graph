using System.Net.Http.Json;
using LifeGraph.Accounts.Csrf;

namespace LifeGraph.IntegrationTests.Infrastructure;

/// <summary>
/// Talks to the API the way the SPA does: cookies kept by the client, and a CSRF token
/// read right before every unsafe request (the token changes with the signed-in user).
/// </summary>
public sealed class SpaClient(HttpClient http) : IDisposable
{
    public HttpClient Http => http;

    public Task<HttpResponseMessage> GetAsync(string path) =>
        http.GetAsync(path, TestContext.Current.CancellationToken);

    public Task<HttpResponseMessage> PostAsync(string path, object body) => SendAsync(HttpMethod.Post, path, body);

    public Task<HttpResponseMessage> DeleteAsync(string path) => SendAsync(HttpMethod.Delete, path, body: null);

    public Task<HttpResponseMessage> LoginAsync(string email, string password) =>
        PostAsync("/api/sessions", new { email, password });

    public Task<HttpResponseMessage> LogoutAsync() => DeleteAsync("/api/sessions/current");

    public Task<HttpResponseMessage> ConfirmEmailAsync(EmailedLink link, string password) =>
        PostAsync("/api/email-confirmations", new { userId = link.UserId, token = link.Token, password });

    public Task<HttpResponseMessage> CompletePasswordResetAsync(EmailedLink link, string newPassword) =>
        PostAsync("/api/password-resets/completion", new { userId = link.UserId, token = link.Token, newPassword });

    public async Task<string> GetCsrfTokenAsync()
    {
        var response = await http.GetFromJsonAsync<CsrfTokenResponse>("/api/csrf-token", TestContext.Current.CancellationToken);
        return response!.Token;
    }

    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, string? csrfToken = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add(CsrfProtection.HeaderName, csrfToken ?? await GetCsrfTokenAsync());
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await http.SendAsync(request, TestContext.Current.CancellationToken);
    }

    public void Dispose() => http.Dispose();
}
