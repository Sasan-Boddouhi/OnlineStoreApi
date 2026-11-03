using System.ComponentModel.DataAnnotations;

namespace BusinessLogic.DTOs.Seller
{
    /// <summary>
    /// M7.11 — مشخصات عمومی فروشگاه که توسط فروشنده مالک قابل ویرایش است.
    /// شامل هیچ فیلد مدیریتی مانند Status یا CommissionRate نمی‌باشد.
    /// </summary>
    public class UpdateSellerProfileDto
    {
        [Required(ErrorMessage = "نام فروشگاه الزامی است.")]
        [MaxLength(150, ErrorMessage = "نام فروشگاه نباید بیش از ۱۵۰ کاراکتر باشد.")]
        public string StoreName { get; set; } = null!;

        [MaxLength(150, ErrorMessage = "شناسه URL نباید بیش از ۱۵۰ کاراکتر باشد.")]
        public string? Slug { get; set; }

        [MaxLength(1000, ErrorMessage = "توضیحات نباید بیش از ۱۰۰۰ کاراکتر باشد.")]
        public string? Description { get; set; }
    }
}
