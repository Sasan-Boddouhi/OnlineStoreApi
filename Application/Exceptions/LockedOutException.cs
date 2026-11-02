using Microsoft.AspNetCore.Http;

namespace Application.Exceptions;

public class LockedOutException : AppException
{
    public DateTime LockoutEnd { get; }
    public int RemainingSeconds { get; }

    public LockedOutException(
        DateTime lockoutEnd,
        string message = "حساب کاربری شما به‌دلیل تلاش‌های ناموفق مکرر موقتاً قفل شده است.",
        string code = "ACCOUNT_LOCKED")
        : base(message, code, StatusCodes.Status403Forbidden)
    {
        LockoutEnd = lockoutEnd;
        var diff = lockoutEnd - DateTime.UtcNow;
        RemainingSeconds = diff.TotalSeconds > 0 ? (int)Math.Ceiling(diff.TotalSeconds) : 0;
    }
}
