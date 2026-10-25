using Application.Common.Specifications;
using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using AutoMapper;
using BusinessLogic.DTOs.Permission;
using BusinessLogic.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace BusinessLogic.Services.Implementations;

public sealed class PermissionService : IPermissionService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    private readonly ILogger<PermissionService> _logger;

    public PermissionService(
        IUnitOfWork unitOfWork,
        IMapper mapper,
        ILogger<PermissionService> logger)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<IReadOnlyList<PermissionDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var entities = await _unitOfWork.Repository<Permission>()
            .ListAsync(new Spec<Permission>(), cancellationToken);

        return entities.Select(e => _mapper.Map<PermissionDto>(e)).ToList();
    }

    public async Task<PermissionDto?> GetByIdAsync(int permissionId, CancellationToken cancellationToken = default)
    {
        var entity = await _unitOfWork.Repository<Permission>().GetByIdAsync(permissionId, cancellationToken);
        if (entity == null)
            return null;

        return _mapper.Map<PermissionDto>(entity);
    }

    public async Task<PermissionDto?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var spec = new Spec<Permission>().Where(p => p.Code == code);
        var entity = await _unitOfWork.Repository<Permission>().FirstOrDefaultAsync(spec, cancellationToken);
        if (entity == null)
            return null;

        return _mapper.Map<PermissionDto>(entity);
    }
}