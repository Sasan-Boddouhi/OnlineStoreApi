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
using BusinessLogic.DTOs.Coupon;
using BusinessLogic.Services.Interfaces;

namespace BusinessLogic.Services.Implementations;

public sealed class CouponService : ICouponService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    public CouponService(IUnitOfWork unitOfWork, IMapper mapper)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
    }

    public async Task<IReadOnlyList<CouponDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var spec = new Spec<Coupon>().OrderBy(c => c.Code);
        var coupons = await _unitOfWork.Repository<Coupon>().ListAsync(spec, cancellationToken);
        return coupons.Select(c => _mapper.Map<CouponDto>(c)).ToList();
    }

    public async Task<CouponDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var coupon = await _unitOfWork.Repository<Coupon>().GetByIdAsync(id, cancellationToken);
        return coupon == null ? null : _mapper.Map<CouponDto>(coupon);
    }

    public async Task<CouponDto?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var coupon = await _unitOfWork.Repository<Coupon>()
            .FirstOrDefaultAsync(new Spec<Coupon>().Where(c => c.Code == code.ToUpperInvariant().Trim()), cancellationToken);
        return coupon == null ? null : _mapper.Map<CouponDto>(coupon);
    }

    public async Task<CouponDto> CreateAsync(CreateCouponDto dto, CancellationToken cancellationToken = default)
    {
        var normalizedCode = dto.Code.ToUpperInvariant().Trim();
        var exists = await _unitOfWork.Repository<Coupon>()
            .AnyAsync(new Spec<Coupon>().Where(c => c.Code == normalizedCode), cancellationToken);

        if (exists)
            throw new BusinessException("کد تخفیف تکراری است.", "COUPON_CODE_DUPLICATE");

        var entity = _mapper.Map<Coupon>(dto);
        entity.Code = normalizedCode;
        await _unitOfWork.Repository<Coupon>().AddAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return _mapper.Map<CouponDto>(entity);
    }

    public async Task<CouponDto?> UpdateAsync(int id, UpdateCouponDto dto, CancellationToken cancellationToken = default)
    {
        var entity = await _unitOfWork.Repository<Coupon>().GetByIdAsync(id, cancellationToken);
        if (entity == null)
            return null;

        var normalizedCode = dto.Code.ToUpperInvariant().Trim();
        var exists = await _unitOfWork.Repository<Coupon>()
            .AnyAsync(new Spec<Coupon>().Where(c => c.Code == normalizedCode && c.CouponId != id), cancellationToken);

        if (exists)
            throw new BusinessException("کد تخفیف تکراری است.", "COUPON_CODE_DUPLICATE");

        _mapper.Map(dto, entity);
        entity.Code = normalizedCode;
        _unitOfWork.Repository<Coupon>().Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return _mapper.Map<CouponDto>(entity);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await _unitOfWork.Repository<Coupon>().GetByIdAsync(id, cancellationToken);
        if (entity == null)
            return false;

        entity.IsActive = false;
        _unitOfWork.Repository<Coupon>().Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }
}
