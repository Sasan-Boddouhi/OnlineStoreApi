namespace BusinessLogic.DTOs.Province
{
    public class UpdateProvinceDto
    {
        public int? ProvinceId { get; set; }
        public string ProvinceName { get; set; } = null!;
    }
}