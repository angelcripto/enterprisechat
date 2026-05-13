using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EnterpriseChat.Protocol.Admin;

namespace EnterpriseChat.Client.Services;

public sealed class AdminApiClient(HttpClient http, SessionContext session)
{
    public async Task<IReadOnlyList<AdminUserDetail>> ListUsersAsync(CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Get, "admin/users", body: null, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<AdminUserDetail>>(ct)) ?? [];
    }

    public async Task<AdminUserDetail> CreateUserAsync(CreateUserRequest request, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Post, "admin/users", request, ct);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            throw new AdminApiException("Ya existe un usuario con ese nombre.");
        }
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var msg = await ExtractErrorAsync(response, ct);
            throw new AdminApiException(msg ?? "Datos no válidos.");
        }
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AdminUserDetail>(ct))!;
    }

    public async Task UpdateUserAsync(int id, UpdateUserRequest request, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Put, $"admin/users/{id}", request, ct);
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            throw new AdminApiException(await ExtractErrorAsync(response, ct) ?? "Datos no válidos.");
        }
        response.EnsureSuccessStatusCode();
    }

    public async Task DeactivateUserAsync(int id, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Delete, $"admin/users/{id}", body: null, ct);
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            throw new AdminApiException(await ExtractErrorAsync(response, ct) ?? "No se pudo desactivar.");
        }
        response.EnsureSuccessStatusCode();
    }

    public async Task ResetPasswordAsync(int id, string newPassword, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Post, $"admin/users/{id}/reset-password",
            new ResetPasswordRequest(newPassword), ct);
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            throw new AdminApiException(await ExtractErrorAsync(response, ct) ?? "Contraseña no válida.");
        }
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<DepartmentSummary>> ListDepartmentsAsync(CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Get, "admin/departments", body: null, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<DepartmentSummary>>(ct)) ?? [];
    }

    public async Task<DepartmentSummary> CreateDepartmentAsync(string name, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Post, "admin/departments",
            new CreateDepartmentRequest(name), ct);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            throw new AdminApiException("Ya existe un departamento con ese nombre.");
        }
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DepartmentSummary>(ct))!;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string relative, object? body, CancellationToken ct)
    {
        if (session.Login is null)
        {
            throw new InvalidOperationException("Sesión no iniciada.");
        }
        var uri = new Uri(new Uri(session.ServerUrl.TrimEnd('/') + "/"), relative);
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.Login.AccessToken);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }
        return await http.SendAsync(request, ct);
    }

    private static async Task<string?> ExtractErrorAsync(HttpResponseMessage r, CancellationToken ct)
    {
        try
        {
            var doc = await r.Content.ReadFromJsonAsync<ErrorEnvelope>(ct);
            return doc?.Error;
        }
        catch
        {
            return null;
        }
    }

    private sealed record ErrorEnvelope(string? Error);
}

public sealed class AdminApiException(string message) : Exception(message);
