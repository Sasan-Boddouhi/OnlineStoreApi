namespace BusinessLogic.DTOs.Warehouse
{
    public class CreateWarehouseDto
    {
        public string Name { get; set; } = null!;
        public int AddressId { get; set; }
        public int? SellerId { get; set; }
    }
}