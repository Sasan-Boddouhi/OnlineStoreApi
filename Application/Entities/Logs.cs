using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Entities
{
    [Table("Logs")]
    public class Logs

    {
        [Key]
        public int Id { get; set; }

        public string Message { get; set; } = string.Empty;
        public string? MessageTemplate { get; set; }
        public string Level { get; set; } = string.Empty;

        public DateTime TimeStamp { get; set; }

        public string? Exception { get; set; }
        public string? Properties { get; set; }

    }
}
