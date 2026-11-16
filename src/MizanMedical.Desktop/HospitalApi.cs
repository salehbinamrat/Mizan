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
    private HttpClient _http;
    private static readonly TimeSpan InteractiveGetTimeout = TimeSpan.FromSeconds(25);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static string UserConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MizanMedical", "mizan-client.json");
    public LoginUser? Staff { get; private set; }
    public bool IsLocalServer => _http.BaseAddress?.IsLoopback == true;
    public string ServerAddress => _http.BaseAddress!.GetLeftPart(UriPartial.Authority);
    public string? ConfigurationIssue { get; private set; }

    private static HttpClient CreateClient(string address, TimeSpan? timeout = null) =>
        new() { BaseAddress = new Uri(address.TrimEnd('/') + "/"),
                Timeout = timeout ?? TimeSpan.FromMinutes(30) };

    public HospitalApi()
    {
        // The login window MUST remain usable when an old/invalid client config is
        // deployed. The remote HTTPS URL is never silently trusted if validation fails.
        _http = CreateClient(ServerEndpointRules.DefaultAddress);
        string path = File.Exists(UserConfigPath)
            ? UserConfigPath
            : Path.Combine(AppContext.BaseDirectory, "mizan-client.json");
        try
        {
            if (File.Exists(path))
            {
                var config = JsonSerializer.Deserialize<ClientConfig>(File.ReadAllText(path), JsonOptions);
                string validated = ServerEndpointRules.Normalize(config?.ServerUrl);
                var replacement = CreateClient(validated);
                var old = _http;
                _http = replacement;
                old.Dispose();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or JsonException or DomainException or UriFormatException)
        {
            ConfigurationIssue = "إعداد اتصال الخادم غير صالح. اختر «إعداد اتصال الخادم» لتصحيحه.";
            StartupDiagnostics.Record("client-configuration", ex);
        }
    }

    public void ConfigureServer(string address)
    {
        string normalized = ServerEndpointRules.Normalize(address);
        string destination = UserConfigPath;
        string directory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(directory);
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            // The app never stores staff credentials or TLS certificate exceptions.
            File.WriteAllText(temporary, JsonSerializer.Serialize(new ClientConfig(normalized)));
            File.Move(temporary, destination, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        var next = CreateClient(normalized);
        var previous = _http;
        _http = next;
        previous.Dispose();
        Staff = null;
        ConfigurationIssue = null;
    }

    public async Task TestServerAsync(string address)
    {
        string normalized = ServerEndpointRules.Normalize(address);
        using var temporary = CreateClient(normalized, TimeSpan.FromSeconds(10));
        using var response = await temporary.GetAsync("api/initial-setup/status");
        response.EnsureSuccessStatusCode();
    }

    public async Task Login(string username, string hospitalName, string password)
    {
        Staff = null;
        _http.DefaultRequestHeaders.Authorization = null;
        var result = await Post<LoginResult>("api/auth/login", new { Username = username, HospitalName = hospitalName, Password = password });
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", result.Token);
        try
        {
            // Verify the bearer token against the authoritative, current server account.
            var current = await Get<LoginUser>("api/auth/me");
            if (current.Id != result.User.Id)
                throw new InvalidOperationException("تعذر تأكيد حساب تسجيل الدخول.");
            Staff = current;
        }
        catch
        {
            Staff = null;
            _http.DefaultRequestHeaders.Authorization = null;
            throw;
        }
    }

    public async Task RefreshSession()
    {
        if (Staff is null) throw new InvalidOperationException("يجب تسجيل الدخول أولا.");
        Staff = await Get<LoginUser>("api/auth/me");
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

    public Task<InitialSetupStatusDto> InitialSetupStatus() =>
        Get<InitialSetupStatusDto>("api/initial-setup/status");

    public Task<IdResult> InitializeOwner(string code, string hospitalName, string fullName, string username, string password, string confirmation) =>
        Post<IdResult>("api/initial-setup/complete", new
        {
            SetupCode = code, HospitalName = hospitalName, FullName = fullName, Username = username,
            Password = password, ConfirmPassword = confirmation
        });

    public Task<List<DepartmentDto>> Departments() => Get<List<DepartmentDto>>("api/departments");
    public Task<List<StaffUserDto>> Users() => Get<List<StaffUserDto>>("api/users");
    public Task<List<string>> PermissionCatalog() => Get<List<string>>("api/permissions/catalog");

    public async Task<T> Put<T>(string path, object payload)
    {
        using var response = await _http.PutAsJsonAsync(path, payload);
        return await Read<T>(response);
    }

    private static string PagedQuery(string path, string? search, int page, int pageSize) =>
        path + "?page=" + page.ToString(System.Globalization.CultureInfo.InvariantCulture) +
        "&pageSize=" + pageSize.ToString(System.Globalization.CultureInfo.InvariantCulture) +
        (string.IsNullOrWhiteSpace(search) ? "" : "&search=" + Uri.EscapeDataString(search.Trim()));

    public Task<List<PatientDto>> Patients(string? search = null, int page = 1, int pageSize = 100) =>
        Get<List<PatientDto>>(PagedQuery("api/patients", search, page, pageSize));
    public Task<List<ServiceDto>> Services() => Get<List<ServiceDto>>("api/services");
    public Task<List<InvoiceDto>> Invoices(string? search = null, int page = 1, int pageSize = 100) =>
        Get<List<InvoiceDto>>(PagedQuery("api/invoices", search, page, pageSize));
    public Task<PersonalDeskSummaryDto> MyCashDeskSummary() => Get<PersonalDeskSummaryDto>("api/cash-desks/mine/summary");
    public Task<List<CashDeskDto>> CashDesks() => Get<List<CashDeskDto>>("api/cash-desks");
    public Task<List<CashShiftDto>> OpenShifts() => Get<List<CashShiftDto>>("api/shifts/open");
    public Task<DailyReportDto> Daily(DateOnly date) => Get<DailyReportDto>("api/reports/daily?day=" + date.ToString("yyyy-MM-dd"));

    public async Task<T> Get<T>(string path)
    {
        // Normal list/report requests must fail promptly on an interrupted LAN.
        // Do not impose this timeout on financial writes or encrypted backups.
        using var timeout = new CancellationTokenSource(InteractiveGetTimeout);
        using var response = await _http.GetAsync(path, timeout.Token);
        return await Read<T>(response, timeout.Token);
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

    private static async Task<T> Read<T>(HttpResponseMessage response,
        CancellationToken cancellation = default)
    {
        string body = await response.Content.ReadAsStringAsync(cancellation);
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

public sealed record InitialSetupStatusDto(bool Required, bool CanInitialize);
public sealed record LoginUser(Guid Id, string Username, string FullName, string Role,
    Guid? DepartmentId, bool AllDepartments, bool IsSystemOwner, List<string> Permissions,
    string? HospitalName = null);
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
    decimal OpeningCash, decimal Received, decimal Spent, decimal Expected,
    decimal? NextOpeningCash = null, bool IsFirstShift = false);
