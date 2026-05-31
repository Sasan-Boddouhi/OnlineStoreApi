namespace BusinessLogic.DTOs.ProductCategory
{
    public class UpdateProductCategoryDto
    {
        public string Name { get; internal set; } = null!;
        public int ProductCategoryId { get; internal set; }
    }
}