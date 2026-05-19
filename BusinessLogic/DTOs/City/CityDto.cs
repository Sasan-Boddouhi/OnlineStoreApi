using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.DTOs.City
{
    public class CityDto
    {
        public int CityId { get; internal set; }
        public string CityName { get; internal set; }
        public int ProvinceId { get; internal set; }
        public string ProvinceName { get; internal set; }
    }
}
