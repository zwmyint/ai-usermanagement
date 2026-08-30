namespace UserManagement.Application.DTOs;

public class PagedRequest
{
    private const int MaxPageSize = 200;
    private int _pageSize = 10;
    private int _page = 1;

    public int Page
    {
        get => _page;
        set => _page = value < 1 ? 1 : value;
    }

    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value switch
        {
            < 1 => 10,
            > MaxPageSize => MaxPageSize,
            _ => value
        };
    }

    public string? Search { get; set; }
    public string? SortBy { get; set; }
    public bool SortDescending { get; set; }

    public int Skip => (Page - 1) * PageSize;
}

public class UserQueryRequest : PagedRequest
{
    public string? Role { get; set; }
    public bool? IsActive { get; set; }
}

public class AuditLogQueryRequest : PagedRequest
{
    public Guid? UserId { get; set; }
    public int? Action { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}

public class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; set; } = Array.Empty<T>();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int FilteredCount { get; set; }
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(FilteredCount / (double)PageSize);
    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < TotalPages;

    public static PagedResult<T> Create(IReadOnlyList<T> items, PagedRequest request, int totalCount, int filteredCount) => new()
    {
        Items = items,
        Page = request.Page,
        PageSize = request.PageSize,
        TotalCount = totalCount,
        FilteredCount = filteredCount
    };
}
