using BusinessLogic.DTOs.Product;

namespace OnlineStore.Tests.Shared.Builders;

public class ProductBuilder
{
    private string _name = "Test Product";
    private decimal _price = 100;
    private string? _description = "Test desc";
    private int _subcategoryId = 1;

    public ProductBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    public ProductBuilder WithPrice(decimal price)
    {
        _price = price;
        return this;
    }

    public ProductBuilder WithSubcategoryId(int id)
    {
        _subcategoryId = id;
        return this;
    }

    public CreateProductDto BuildCreateDto()
    {
        return new CreateProductDto
        {
            Name = _name,
            Price = _price,
            Description = _description,
            SubcategoryId = _subcategoryId
        };
    }
}