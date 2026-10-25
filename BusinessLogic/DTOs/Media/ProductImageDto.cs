namespace BusinessLogic.DTOs.Media
{
    public class ProductImageDto
    {
        public int ProductImageId { get; set; }
        public int ProductId { get; set; }
        public string Url { get; set; } = null!;
        public string? Alt { get; set; }
        public int SortOrder { get; set; }
        public bool IsPrimary { get; set; }
    }
}