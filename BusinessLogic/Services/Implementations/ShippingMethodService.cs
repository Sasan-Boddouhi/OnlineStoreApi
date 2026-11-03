using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Application.Common.Specifications;
using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using AutoMapper;
using BusinessLogic.DTOs.Shipping;
using BusinessLogic.Services.Interfaces;

namespace BusinessLogic.Services.Implementations;

public sealed class ShippingMethodService : IShippingMethodService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    public ShippingMethodService(IUnitOfWork unitOfWork, IMapper mapper)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
    }

    public async Task<IReadOnlyList<ShippingMethodDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var spec = new Spec<ShippingMethod>().Where(s => s.IsActive);
        var methods = await _unitOfWork.Repository<ShippingMethod>().ListAsync(spec, cancellationToken);
        return methods.Select(m => _mapper.Map<ShippingMethodDto>(m)).ToList();
    }

    public async Task<ShippingMethodDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var method = await _unitOfWork.Repository<ShippingMethod>().GetByIdAsync(id, cancellationToken);
        return method == null ? null : _mapper.Map<ShippingMethodDto>(method);
    }

    public async Task<ShippingMethodDto> CreateAsync(CreateShippingMethodDto dto, CancellationToken cancellationToken = default)
    {
        var exists = await _unitOfWork.Repository<ShippingMethod>()
            .AnyAsync(new Spec<ShippingMethod>().Where(s => s.Slug == dto.Slug), cancellationToken);

        if (exists)
            throw new BusinessException("روش ارسال با این شناسه (Slug) تکراری است.", "SHIPPING_METHOD_SLUG_DUPLICATE");

        var entity = _mapper.Map<ShippingMethod>(dto);
        await _unitOfWork.Repository<ShippingMethod>().AddAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return _mapper.Map<ShippingMethodDto>(entity);
    }

    public async Task<ShippingMethodDto?> UpdateAsync(int id, UpdateShippingMethodDto dto, CancellationToken cancellationToken = default)
    {
        var entity = await _unitOfWork.Repository<ShippingMethod>().GetByIdAsync(id, cancellationToken);
        if (entity == null)
            return null;

        _mapper.Map(dto, entity);
        _unitOfWork.Repository<ShippingMethod>().Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return _mapper.Map<ShippingMethodDto>(entity);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await _unitOfWork.Repository<ShippingMethod>().GetByIdAsync(id, cancellationToken);
        if (entity == null)
            return false;

        entity.IsActive = false;
        _unitOfWork.Repository<ShippingMethod>().Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }
}
