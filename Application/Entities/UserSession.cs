using Application.Entities;
using System.ComponentModel.DataAnnotations;

public sealed class UserSession
{
    public Guid Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    [StringLength(200)]
    public string DeviceId { get; set; } = null!;
    public string? DeviceName { get; set; }

    [StringLength(45)] // IPv6 max length
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime LastActivityUtc { get; set; }

    public DateTime AbsoluteExpiryUtc { get; set; }

    public SessionStatus Status { get; set; }
    public DateTime? RevokedAtUtc { get; set; }

    [Timestamp]
    public byte[] RowVersion { get; set; } = null!;

    public ICollection<RefreshTokenEntity> RefreshTokens { get; set; }
        = new List<RefreshTokenEntity>();


    public bool IsIdleExpired(TimeSpan idleTimeout)
    {
        return LastActivityUtc.Add(idleTimeout) <= DateTime.UtcNow;
    }

    public bool IsAbsoluteExpired()
    {
        return AbsoluteExpiryUtc <= DateTime.UtcNow;
    }


    public enum SessionStatus
    {
        Active = 1,
        Revoked = 2,
        Expired = 3,
        Suspicious = 4
    }
}