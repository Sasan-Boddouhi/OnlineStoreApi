namespace BusinessLogic.DTOs.City
{
    public class UpdateCityDto
    {
        public int CityId { get; set; }
        public string CityName { get; internal set; } = null!;
    }
}