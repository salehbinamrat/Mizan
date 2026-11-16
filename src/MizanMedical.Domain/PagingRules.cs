namespace MizanMedical.Domain;

public static class PagingRules
{
    /// <summary>Bounded, stable offset paging for searchable hospital work lists.</summary>
    public static (int Offset, int PageSize) Validate(int page, int pageSize)
    {
        if (page is < 1 or > 100_000)
            throw new DomainException("رقم الصفحة يجب أن يكون بين 1 و100000.");
        if (pageSize is < 1 or > 200)
            throw new DomainException("حجم الصفحة يجب أن يكون بين 1 و200 سجلا.");
        return (checked((page - 1) * pageSize), pageSize);
    }
}
