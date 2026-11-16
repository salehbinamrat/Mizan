namespace MizanMedical.Domain;

/// <summary>
/// Server address validation shared by the Windows client and startup tests.
/// Credentials and API paths must never be included in the saved connection URL.
/// </summary>
public static class ServerEndpointRules
{
    public const string DefaultAddress = "https://localhost:5081";

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2048 ||
            !Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? uri) ||
            string.IsNullOrWhiteSpace(uri.Host))
            throw new DomainException("أدخل عنوان خادم صحيحا، مثل https://192.168.1.10:5081.");

        bool isHttps = uri.Scheme == Uri.UriSchemeHttps;
        bool isLocalHttp = uri.IsLoopback && uri.Scheme == Uri.UriSchemeHttp;
        if (!isHttps && !isLocalHttp)
            throw new DomainException("الاتصال عبر الشبكة يجب أن يستخدم HTTPS بشهادة موثوقة؛ HTTP مسموح على الجهاز المحلي فقط.");
        if (uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            uri.AbsolutePath != "/")
            throw new DomainException("احفظ عنوان الخادم وبورته فقط، دون مسار API أو كلمة مرور أو بيانات إضافية.");

        return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
    }
}
