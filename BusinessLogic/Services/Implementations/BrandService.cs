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
using BusinessLogic.DTOs.Brand;
using BusinessLogic.Services.Interfaces;

namespace BusinessLogic.Services.Implementations;

public sealed class BrandService : IBrandService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    public BrandService(IUnitOfWork unitOfWork, IMapper mapper)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
    }

    public async Task<IReadOnlyList<BrandDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var spec = new Spec<Brand>().Where(b => b.IsActive).OrderBy(b => b.Name);
        var brands = await _unitOfWork.Repository<Brand>().ListAsync(spec, cancellationToken);
        return brands.Select(b => _mapper.Map<BrandDto>(b)).ToList();
    }

    public async Task<BrandDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var brand = await _unitOfWork.Repository<Brand>().GetByIdAsync(id, cancellationToken);
        return brand == null ? null : _mapper.Map<BrandDto>(brand);
    }

    public async Task<BrandDto> CreateAsync(CreateBrandDto dto, CancellationToken cancellationToken = default)
    {
        var exists = await _unitOfWork.Repository<Brand>()
            .AnyAsync(new Spec<Brand>().Where(b => b.Slug == dto.Slug), cancellationToken);

        if (exists)
            throw new BusinessException("برند با این شناسه (Slug) تکراری است.", "BRAND_SLUG_DUPLICATE");

        var entity = _mapper.Map<Brand>(dto);
        await _unitOfWork.Repository<Brand>().AddAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return _mapper.Map<BrandDto>(entity);
    }

    public async Task<BrandDto?> UpdateAsync(int id, UpdateBrandDto dto, CancellationToken cancellationToken = default)
    {
        var entity = await _unitOfWork.Repository<Brand>().GetByIdAsync(id, cancellationToken);
        if (entity == null)
            return null;

        var exists = await _unitOfWork.Repository<Brand>()
            .AnyAsync(new Spec<Brand>().Where(b => b.Slug == dto.Slug && b.BrandId != id), cancellationToken);

        if (exists)
            throw new BusinessException("برند با این شناسه (Slug) تکراری است.", "BRAND_SLUG_DUPLICATE");

        _mapper.Map(dto, entity);
        _unitOfWork.Repository<Brand>().Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return _mapper.Map<BrandDto>(entity);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await _unitOfWork.Repository<Brand>().GetByIdAsync(id, cancellationToken);
        if (entity == null)
            return false;

        entity.IsActive = false;
        _unitOfWork.Repository<Brand>().Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }
}
