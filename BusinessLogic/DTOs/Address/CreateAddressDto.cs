using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.DTOs.Address
{
    public class CreateAddressDto
    {
        [Required]
        public int CityId { get; set; }

        [Required]
        [MaxLength(10)]
        public string Plaque { get; set; } = null!;

        [Required]
        [MaxLength(20)]
        public string Unit { get; set; } = null!;

        [Required]
        [MaxLength(20)]
        public string PostalCode { get; set; } = null!;

        [Required]
        [MaxLength(50)]
        public string RecipientFirstName { get; set; } = null!;

        [Required]
        [MaxLength(50)]
        public string RecipientLastName { get; set; } = null!;

        [MaxLength(250)]
        public string? ExtraDescription { get; set; }

        public bool IsDefault { get; set; }
    }
}
