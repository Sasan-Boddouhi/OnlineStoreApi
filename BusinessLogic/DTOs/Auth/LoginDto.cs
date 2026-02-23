using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.DTOs.Auth
{
    public class LoginDto
    {
        public string PhoneNumber { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        public string? DeviceName { get; set; }
        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }
    }
}
