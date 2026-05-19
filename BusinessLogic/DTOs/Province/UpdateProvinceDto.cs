using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.DTOs.Province
{
    public class UpdateProvinceDto
    {
        public int? ProvinceId { get; internal set; }
        public string ProvinceName { get; internal set; }
    }
}
