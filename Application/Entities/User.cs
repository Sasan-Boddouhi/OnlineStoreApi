using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Application.Entities
{

    [Table("User")]
    public class User : AuditableEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int UserId { get; set; }

        [Required]
        [StringLength(100)]
        public required string FirstName { get; set; }

        [Required]
        [StringLength(100)]
        public required string LastName { get; set; }

        [Required]
        [DataType(DataType.Password)]
        public required string PasswordHash { get; set; }

        [Required]
        public required UserType UserType { get; set; }

        public DateTime? DateOfBirth { get; set; }

        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Batch 44 (پاسخ مالک به Batch 49b): حساب سیستمی بی‌اثر — مثلاً مالک نشانی انبار پلتفرم که هنگام راه‌اندازی
        /// ساخته می‌شود. با هیچ مسیری (رمز، OTP، بازیابی رمز، refresh) وارد نمی‌شود، از هیچ API فعال، ویرایش یا نقش‌دار
        /// نمی‌شود و در فهرست کاربران ادمین با <c>isSystem</c> مشخص است. فقط سرویس‌های راه‌اندازی آن را می‌سازند.
        /// </summary>
        public bool IsSystem { get; set; }

        /// <summary>
        /// Batch 44: تنها قاعدهٔ «این حساب می‌تواند وارد شود / توکن بگیرد». ورود با رمز و refresh از آن استفاده می‌کنند و
        /// OTP و بازیابی رمز (Batch 46 و 47) هم باید از همین استفاده کنند.
        /// </summary>
        [NotMapped]
        public bool CanSignIn => IsActive && !IsSystem;

        [NotMapped]
        public string FullName => $"{FirstName} {LastName}";

        // Navigation properties برای اطلاعات کاربر
        [MaxLength(256)]
        public string? Email { get; set; }
        public virtual ICollection<Address> Addresses { get; set; } = new HashSet<Address>();
        public ICollection<UserSession> Sessions { get; set; } = new List<UserSession>();

        [Required]
        [StringLength(11)]
        public string PhoneNumber { get; set; } = string.Empty;

        /// <summary>
        /// Batch 46 (N9): مالک شماره با کد OTP ثابت کرده که شماره مال اوست. ثبت‌نام فقط با کد تأییدشده کامل می‌شود و
        /// همیشه true می‌گذارد؛ کاربرانی که پیش از Batch 46 بودند در migration تأییدشده علامت خوردند (به‌جز حساب سیستمی).
        /// کاربری که ادمین می‌سازد تأییدنشده است. بوت‌استرپ SuperAdmin فقط شمارهٔ تأییدشده را می‌پذیرد.
        /// </summary>
        public bool PhoneNumberConfirmed { get; set; }

        // Navigation properties برای Customer و Employee و Seller و Cart
        public virtual Customer? Customer { get; set; }
        public virtual Employee? Employee { get; set; }
        public virtual Seller? Seller { get; set; }
        public virtual Cart? Cart { get; set; }

        // نقش‌های کاربر (Phase 1 / R45) — جدول اتصال چند-نقشی
        public virtual ICollection<UserRole> UserRoles { get; set; } = new HashSet<UserRole>();

        [MaxLength(36)]
        public string SecurityStamp { get; set; } = Guid.NewGuid().ToString();

        public int FailedLoginAttempts { get; set; } = 0;

        public DateTime? LockoutEnd { get; set; }

    }

    public enum UserType
    {
        [Display(Name = "مشتری")]
        Customer = 1,

        [Display(Name = "کارمند")]
        Employee = 2,
    }
}
