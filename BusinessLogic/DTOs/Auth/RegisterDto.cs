using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.DTOs.Auth
{
    public class RegisterDto
    {
        public string FirstName { get; set; } = null!;
        public string LastName { get; set; } = null!;
        public string PhoneNumber { get; set; } = null!;
        public string Password { get; set; } = null!;
        public string? Email { get; set; }
        public string? DateOfBirth { get; set; }

        /// <summary>
        /// Batch 46 (N9): توکنی که <c>auth/otp/verify</c> برای همین شماره و هدف ثبت‌نام داده است. بدون آن (یا با توکن
        /// نادرست، منقضی یا مصرف‌شده) ثبت‌نام 422 <c>PHONE_NOT_VERIFIED</c> می‌گیرد.
        /// </summary>
        public string? PhoneVerificationToken { get; set; }

        public string? DeviceId { get; set; }
        public string? DeviceName { get; set; }

        // F-20: IpAddress و UserAgent دیگر از بدنه خوانده نمی‌شوند؛ اگر کلاینت بفرستد نادیده گرفته می‌شوند
        // و مقدار واقعی از خود درخواست HTTP می‌آید (ClientRequestInfo).
    }
}
