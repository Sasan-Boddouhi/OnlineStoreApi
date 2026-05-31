namespace BusinessLogic.DTOs.Province
{
    public class UpdateProvinceDto
    {
        public int? ProvinceId { get; internal set; }
        public string ProvinceName { get; internal set; } = null!;
    }
}