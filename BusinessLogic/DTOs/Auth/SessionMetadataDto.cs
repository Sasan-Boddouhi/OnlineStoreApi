namespace BusinessLogic.DTOs.Auth;


public sealed record SessionMetadataDto
(
    string? DeviceId,
    string? DeviceName,
    string? IpAddress,
    string? UserAgent
);