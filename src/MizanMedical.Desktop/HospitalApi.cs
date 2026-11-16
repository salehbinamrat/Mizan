using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using MizanMedical.Domain;
using System.Text.Json;

namespace MizanMedical.Desktop;

public sealed class HospitalApi
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    public LoginUser? Staff { get; private set; }

    public HospitalApi()
    {
        string configPath = Path.Combine(AppContext.BaseDirectory, "mizan-client.json");
        string url = "https://localhost:5081";
        if (File.Exists(configPath))
        {
            var config = JsonSerializer.Deserialize<ClientConfig>(File.ReadAllText(configPath), JsonOptions);
            url = config?.ServerUrl ?? url;
        }
        var uri = new Uri(url);
        if (uri.Scheme != Uri.UriSchemeHttps && !(uri.IsLoopback && uri.Scheme == Uri.UriSchemeHttp))
            throw new InvalidOperationException("لا يمكن استخدام HTTP على شبكة المستشفى. اضبط HTTPS بشهادة موثوقة.");
        _http = new HttpClient { BaseAddress = new Uri(uri.ToString().TrimEnd('/') + "/"), Timeout = TimeSpan.FromMinutes(30) };
    }

    public async Task Login(string username, string password)
    {
        var result = await Post<LoginResult>("api/auth/login", new { Username = username, Password = password });
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", result.Token);
        Staff = result.User;
    }

    public async Task Logout()
    {
        try { if (Staff is not null) await PostWithoutResult("api/auth/logout", new { }); }
        finally { Staff = null; _http.DefaultRequestHeaders.Authorization = null; }
    }

    public async Task<string> DownloadFullBackup(string passphrase, string destination)
    {
        if (Staff?.IsSystemOwner != true)
            throw new UnauthorizedAccessException("النسخة الشاملة متاحة لمسؤول البرنامج الأساسي فقط.");
        string temporary = destination + ".partial";
        if (File.Exists(temporary)) throw new IOException("يوجد ملف مؤقت بنفس الاسم؛ اختر اسما جديدا.");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/admin/backup/download")
            { Content = JsonContent.Create(new { Passphrase = passphrase }) };
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode) await Read<JsonElement>(response);
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 1024 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await response.Content.CopyToAsync(file);
                await file.FlushAsync();
            }
            await using (var stream = File.OpenRead(temporary))
                await EncryptedBackupFormat.VerifyAsync(stream, passphrase);
            File.Move(temporary, destination, true);
            await using var verifiedFile = File.OpenRead(destination);
            string checksum = Convert.ToHexString(await SHA256.HashDataAsync(verifiedFile));
            await File.WriteAllTextAsync(destination + ".sha256", checksum);
            return checksum;
        }
        catch
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            throw;
        }
    }

    public Task<List<DepartmentDto>> Departments() => Get<List<DepartmentDto>>("api/departments");
    public Task<List<StaffUserDto>> Users() => Get<List<StaffUserDto>>("api/users");
    public Task<List<string>> PermissionCatalog() => Get<List<string>>("api/permissions/catalog");

    public async Task<T> Put<T>(string path, object payload)
    {
        using var response = await _http.PutAsJsonAsync(path, payload);
        return await Read<T>(response);
    }

    public Task<List<PatientDto>> Patients() => Get<List<PatientDto>>("api/patients");
    public Task<List<ServiceDto>> Services() => Get<List<ServiceDto>>("api/services");
    public Task<List<InvoiceDto>> Invoices() => Get<List<InvoiceDto>>("api/invoices");
    public Task<PersonalDeskSummaryDto> MyCashDeskSummary() => Get<PersonalDeskSummaryDto>("api/cash-desks/mine/summary");
    public Task<List<CashDeskDto>> CashDesks() => Get<List<CashDeskDto>>("api/cash-desks");
    public Task<List<CashShiftDto>> OpenShifts() => Get<List<CashShiftDto>>("api/shifts/open");
    public Task<DailyReportDto> Daily(DateOnly date) => Get<DailyReportDto>("api/reports/daily?day=" + date.ToString("yyyy-MM-dd"));

    public async Task<T> Get<T>(string path)
    {
        using var response = await _http.GetAsync(path);
        return await Read<T>(response);
    }

    public async Task<T> Post<T>(string path, object request)
    {
        using var response = await _http.PostAsJsonAsync(path, request);
        return await Read<T>(response);
    }

    public async Task PostWithoutResult(string path, object request)
    {
        using var response = await _http.PostAsJsonAsync(path, request);
        if (!response.IsSuccessStatusCode) await Read<JsonElement>(response);
    }

    private static async Task<T> Read<T>(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            string error = $"الخادم أعاد الحالة {(int)response.StatusCode}.";
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("detail", out var detail)) error = detail.GetString() ?? error;
                else if (doc.RootElement.TryGetProperty("title", out var title)) error = title.GetString() ?? error;
            }
            catch (JsonException) { }
            throw new InvalidOperationException(error);
        }
        return JsonSerializer.Deserialize<T>(body, JsonOptions) ?? throw new InvalidOperationException("استجابة غير صالحة من الخادم.");
    }

    private sealed record ClientConfig(string ServerUrl);
    private sealed record LoginResult(string Token, LoginUser User);
}

public sealed record LoginUser(Guid Id, string Username, string FullName, string Role,
    Guid? DepartmentId, bool AllDepartments, bool IsSystemOwner, List<string> Permissions);
public sealed record DepartmentDto(Guid Id, string Code, string Name, int MaxActiveUsers);
public sealed record DepartmentCapacityDto(Guid Id, int MaxActiveUsers, int ActiveUsers);
public sealed record StaffUserDto(Guid Id, string Username, string FullName, string Role,
    bool IsActive, bool IsSystemOwner, Guid? DepartmentId, bool AllDepartments,
    List<string> Permissions);

public sealed record PatientDto(Guid Id, string FileNumber, string FullName)
{
    public string Display => $"{FileNumber} — {FullName}";
}
public sealed record ServiceDto(Guid Id, string Code, string Name, string Department, decimal Price)
{
    public string Display => $"{Code} — {Name} ({Price:N2})";
}
public sealed record InvoiceDto(Guid Id, string InvoiceNumber, DateTime IssuedAtUtc, decimal Total, decimal Paid, decimal Balance, string? PatientName)
{
    public string Display => $"{InvoiceNumber} — {PatientName} — المتبقي: {Balance:N2}";
}
public sealed record CashDeskDto(Guid Id, string Name);
public sealed record CashShiftDto(Guid Id, Guid CashDeskId, decimal OpeningCash, DateTime OpenedAtUtc)
{
    public string Display => $"{OpenedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm} — {Id.ToString("N")[..8]}";
}
public sealed record DailyReportDto(string Day, string Currency, int InvoiceCount,
    decimal InvoicedAmount, decimal CashReceived, decimal CashSpent, decimal NetCashMovement, string Note);
public sealed record IdResult(Guid Id);
public sealed record InvoiceCreated(Guid Id, string InvoiceNumber, decimal Total, decimal Paid);
public sealed record ShiftClosed(Guid Id, decimal OpeningCash, decimal ExpectedCash, decimal CountedCash, decimal Variance, DateTime ClosedAtUtc);

public sealed record PersonalDeskSummaryDto(Guid Id, string Name, Guid? DepartmentId, Guid? ShiftId,
    decimal OpeningCash, decimal Received, decimal Spent, decimal Expected);
