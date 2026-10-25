namespace BusinessLogic.DTOs.Shared
{
    /// <summary>
    /// Phase 9 (R32) — نتیجهٔ صفحه‌بندی‌شده.
    /// فراداده همراه داده برگردانده می‌شود تا کلاینت مجبور به حدس زدن نباشد.
    /// </summary>
    public class PagedResultDto<T>
    {
        public IReadOnlyList<T> Items { get; set; } = Array.Empty<T>();
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }
        public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

        public PagedResultDto() { }

        public PagedResultDto(IReadOnlyList<T> items, int pageNumber, int pageSize, int totalCount)
        {
            Items = items;
            PageNumber = pageNumber;
            PageSize = pageSize;
            TotalCount = totalCount;
        }
    }
}