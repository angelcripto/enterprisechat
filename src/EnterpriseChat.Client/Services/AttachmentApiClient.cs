using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EnterpriseChat.Protocol.Files;

namespace EnterpriseChat.Client.Services;

public sealed class AttachmentApiClient(HttpClient http, SessionContext session)
{
    public async Task<AttachmentSummary> UploadAsync(string filePath, CancellationToken ct = default)
    {
        if (session.Login is null)
        {
            throw new InvalidOperationException("No hay sesión iniciada.");
        }

        var endpoint = new Uri(new Uri(session.ServerUrl.TrimEnd('/') + "/"), "files");
        await using var fs = File.OpenRead(filePath);

        using var form = new MultipartFormDataContent();
        var stream = new StreamContent(fs);
        stream.Headers.ContentType = new MediaTypeHeaderValue(GuessMimeType(filePath));
        form.Add(stream, "file", Path.GetFileName(filePath));

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.Login.AccessToken);

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var msg = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Upload falló ({(int)response.StatusCode}): {msg}");
        }

        return (await response.Content.ReadFromJsonAsync<AttachmentSummary>(ct))!;
    }

    public async Task DownloadToAsync(long attachmentId, string destinationPath, CancellationToken ct = default)
    {
        if (session.Login is null)
        {
            throw new InvalidOperationException("No hay sesión iniciada.");
        }

        var endpoint = new Uri(new Uri(session.ServerUrl.TrimEnd('/') + "/"), $"files/{attachmentId}");
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.Login.AccessToken);

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var fs = File.Create(destinationPath);
        await response.Content.CopyToAsync(fs, ct);
    }

    private static string GuessMimeType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".pdf" => "application/pdf",
            ".txt" or ".md" => "text/plain",
            ".zip" => "application/zip",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            _ => "application/octet-stream"
        };
    }
}
