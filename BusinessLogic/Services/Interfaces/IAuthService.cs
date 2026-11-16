using BusinessLogic.DTOs.Auth;
using BusinessLogic.DTOs.User;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.Services.Interfaces
{
    public interface IAuthService
    {
        // F-20: client همان IP و User-Agent خوانده‌شده از خود درخواست HTTP است، نه از بدنه.
        Task<AuthResultDto> RegisterAsync(RegisterDto dto, ClientRequestInfo client, CancellationToken cancellationToken = default);
        Task<AuthResultDto?> LoginAsync(LoginDto dto, ClientRequestInfo client, CancellationToken cancellationToken = default);
        Task<AuthResultDto?> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default);
        Task LogoutSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);
        Task LogoutAllAsync(int userId, CancellationToken cancellationToken = default);
    }
}
