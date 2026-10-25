namespace BusinessLogic.DTOs.Warehouse
{
    public class WarehouseDto
    {
        public int WarehouseId { get; set; }
        public string Name { get; set; } = null!;
        public int AddressId { get; set; }
        public string AddressName { get; set; } = null!;
        public int? SellerId { get; set; }
        public string SellerName { get; set; } = null!;
    }
}