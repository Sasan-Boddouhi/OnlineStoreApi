using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.DTOs.Address
{
    public class AddressDto
    {
        public string Plaque { get; set; }
        public int CityId { get; set; }
        public int UserId { get; internal set; }
        public string Unit { get; set; }
        public  string PostalCode { get; set; }
        public  string RecipientFirstName { get; set; }
        public  string RecipientLastName { get; set; }
    }
}
