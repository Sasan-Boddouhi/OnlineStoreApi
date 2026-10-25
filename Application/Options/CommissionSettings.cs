namespace Application.Options
{
    /// <summary>
    /// Phase 3 — تنظیمات کمیسیون پلتفرم.
    /// نرخ نهایی هر گروه = Seller.CommissionRate (اگر مقدار دارد) وگرنه DefaultRate.
    /// </summary>
    public sealed class CommissionSettings
    {
        public const string SectionName = "CommissionSettings";

        /// <summary>نرخ پیش‌فرض کمیسیون به درصد. پیش‌فرض ۱۰٪.</summary>
        public decimal DefaultRate { get; set; } = 10m;
    }
}