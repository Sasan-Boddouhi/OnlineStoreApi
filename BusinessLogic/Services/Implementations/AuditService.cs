using Application.Entities;
using Application.Interfaces;
using BusinessLogic.Services.Interfaces;

namespace BusinessLogic.Services.Implementations;

/// <summary>
/// Phase 9 (R40) — ثبت رویداد در AuditLog بدون ذخیره‌سازی.
/// </summary>
public sealed class AuditService : IAuditService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;

    public AuditService(IUnitOfWork unitOfWork, ICurrentUserService currentUser)
    {
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
    }

    public async Task RecordAsync(
        string action,
        string? entityName = null,
        string? entityId = null,
        string? details = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(action))
            throw new ArgumentException("Action is required.", nameof(action));

        await _unitOfWork.Repository<AuditLog>().AddAsync(
            new AuditLog
            {
                Action = action,
                EntityName = entityName,
                EntityId = entityId,
                UserId = _currentUser.TryGetCurrentUserId(),
                Details = details
            },
            cancellationToken);
    }
}