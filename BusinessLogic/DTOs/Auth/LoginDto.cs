using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.DTOs.Auth
{
    public class LoginDto
    {
        public string PhoneNumber { get; set; }
        public string Password { get;  set; }
    }
}
