namespace BusinessLogic.DTOs.City
{
    public class CityDto
    {
        public int CityId { get; set; }
        public string CityName { get; set; } = null!;
        public int ProvinceId { get; set; }
        public string ProvinceName { get; set; } = null!;
    }
}