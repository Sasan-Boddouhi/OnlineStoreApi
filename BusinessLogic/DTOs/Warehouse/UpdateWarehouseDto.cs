namespace BusinessLogic.DTOs.Warehouse
{
    public class UpdateWarehouseDto
    {
        public int WarehouseId { get; set; }
        public string Name { get; set; } = null!;
        public int AddressId { get; set; }
        public int? SellerId { get; set; }
    }
}