using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;


namespace Application.Entities
{
    [NotMapped]
    public abstract class AuditableEntity
    {
        [Required]
        public DateTime CreatedOn { get; set; }

        public int? CreatedById { get; set; }

        [Required]
        public DateTime ModifiedOn { get; set; }

        public int? ModifiedById { get; set; }
    }
}
