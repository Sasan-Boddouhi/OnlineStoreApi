namespace BusinessLogic.DTOs.ProductCategory
{
    public class ProductCategoryDto
    {
        public int ProductCategoryId { get; internal set; }
        public string Name { get; internal set; } = null!;
        public bool IsActive { get; internal set; }
    }
}