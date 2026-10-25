namespace BusinessLogic.DTOs.City
{
    public class CityDto
    {
        public int CityId { get; internal set; }
        public string CityName { get; internal set; } = null!;
        public int ProvinceId { get; internal set; }
        public string ProvinceName { get; internal set; } = null!;
    }
}