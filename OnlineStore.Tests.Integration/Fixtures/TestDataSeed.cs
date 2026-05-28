using Application.Entities;
using DataLayer.Context;

public static class TestDataSeed
{
    public static async Task SeedAsync(AppDbContext db)
    {
        if (!db.ProductCategory.Any())
        {
            var cat = new ProductCategory
            {
                CategoryName = "Test Category"
            };

            db.ProductCategory.Add(cat);
            await db.SaveChangesAsync();
        }

        var category = db.ProductCategory.First();

        if (!db.ProductSubcategory.Any())
        {
            db.ProductSubcategory.Add(new ProductSubcategory
            {
                SubcategoryName = "Test Subcategory",
                CategoryId = category.CategoryId
            });

            await db.SaveChangesAsync();
        }
    }
}