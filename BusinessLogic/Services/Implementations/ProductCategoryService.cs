using Application.Entities;
using Application.Interfaces;
using Application.Common.Specifications;
using AutoMapper;
using BusinessLogic.DTOs.ProductCategory;
using BusinessLogic.Services.Interfaces;
using Microsoft.Extensions.Logging;
using BusinessLogic.Specifications.ProductCategories;

namespace BusinessLogic.Services.Implementations;

public sealed class ProductCategoryService : IProductCategoryService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<ProductCategoryService> _logger;

    public ProductCategoryService(
        IUnitOfWork unitOfWork,
        IMapper mapper,
        ICurrentUserService currentUserService,
        ILogger<ProductCategoryService> logger)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    private void EnsureAdmin()
    {
        var role = _currentUserService.GetCurrentUserRole();
        if (role != "Admin" && role != "Manager")
            throw new UnauthorizedAccessException("Access denied. Only Admin or Manager can perform this action.");
    }

    #region Create

    public async Task<ProductCategoryDto> CreateAsync(CreateProductCategoryDto dto, CancellationToken cancellationToken = default)
    {
        try
        {
            EnsureAdmin();

            var exists = await _unitOfWork.Repository<ProductCategory>()
                .AnyAsync(pc => pc.CategoryName == dto.Name, cancellationToken);
            if (exists)
                throw new Exception("Category with this name already exists.");

            var entity = _mapper.Map<ProductCategory>(dto);
            await _unitOfWork.Repository<ProductCategory>().AddAsync(entity, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Category created by {User} at {Time}. Id={Id}, Name={Name}",
                _currentUserService.GetCurrentUserName(), DateTime.Now, entity.CategoryId, entity.CategoryName);

            return _mapper.Map<ProductCategoryDto>(entity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while creating category by {User} at {Time}. Input={@Dto}",
                _currentUserService.GetCurrentUserName(), DateTime.Now, dto);
            throw;
        }
    }

    #endregion

    #region Delete

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        try
        {
            EnsureAdmin();

            var entity = await _unitOfWork.Repository<ProductCategory>().GetByIdAsync(id, cancellationToken);
            if (entity == null || !entity.IsActive)
            {
                _logger.LogWarning("Delete failed. Category with Id={Id} not found or inactive. User={User}, Time={Time}",
                    id, _currentUserService.GetCurrentUserName(), DateTime.Now);
                return false;
            }

            _unitOfWork.Repository<ProductCategory>().Delete(entity);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Category deleted successfully by {User} at {Time}. Deleted id={Id}",
                _currentUserService.GetCurrentUserName(), DateTime.Now, id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while deleting category. Id={Id}, User={User}, Time={Time}",
                id, _currentUserService.GetCurrentUserName(), DateTime.Now);
            throw;
        }
    }

    #endregion

    #region Get All (با استفاده از Spec)

    public async Task<IEnumerable<ProductCategoryDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var spec = new Spec<ProductCategory>()
                .Where(pc => pc.IsActive)
                .OrderBy(pc => pc.CategoryName);

            var categories = await _unitOfWork.Repository<ProductCategory>()
                .ListAsync(spec, ProductCategoryQueryConfig.Projection, cancellationToken);

            _logger.LogInformation("{Count} categories fetched by {User} at {Time}.",
                categories.Count, _currentUserService.GetCurrentUserName(), DateTime.Now);

            return categories;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while fetching categories by {User} at {Time}.",
                _currentUserService.GetCurrentUserName(), DateTime.Now);
            throw;
        }
    }

    #endregion

    #region Get By Id

    public async Task<ProductCategoryDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        try
        {
            var spec = new Spec<ProductCategory>()
                .Where(pc => pc.CategoryId == id)
                .Where(pc => pc.IsActive);

            var dto = await _unitOfWork.Repository<ProductCategory>()
                .FirstOrDefaultAsync(spec, ProductCategoryQueryConfig.Projection, cancellationToken);

            if (dto == null)
                _logger.LogWarning("Category with Id={Id} not found. Requested by {User} at {Time}.",
                    id, _currentUserService.GetCurrentUserName(), DateTime.Now);
            else
                _logger.LogInformation("Category fetched by {User} at {Time}. Id={Id}",
                    _currentUserService.GetCurrentUserName(), DateTime.Now, id);

            return dto;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while fetching category Id={Id} by {User} at {Time}",
                id, _currentUserService.GetCurrentUserName(), DateTime.Now);
            throw;
        }
    }

    #endregion

    #region Update

    public async Task<ProductCategoryDto?> UpdateAsync(UpdateProductCategoryDto dto, CancellationToken cancellationToken = default)
    {
        try
        {
            EnsureAdmin();

            var exists = await _unitOfWork.Repository<ProductCategory>()
                .AnyAsync(pc => pc.CategoryName == dto.Name && pc.CategoryId != dto.ProductCategoryId, cancellationToken);
            if (exists)
                throw new Exception("Category with this name already exists.");

            var entity = await _unitOfWork.Repository<ProductCategory>().GetByIdAsync(dto.ProductCategoryId, cancellationToken);
            if (entity == null || !entity.IsActive)
            {
                _logger.LogWarning("Update failed. Category with Id={Id} not found or inactive.", dto.ProductCategoryId);
                return null;
            }

            _mapper.Map(dto, entity);
            _unitOfWork.Repository<ProductCategory>().Update(entity);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var updatedDto = _mapper.Map<ProductCategoryDto>(entity);
            _logger.LogInformation("Category updated by {User} at {Time}. Id={Id}",
                _currentUserService.GetCurrentUserName(), DateTime.Now, entity.CategoryId);
            return updatedDto;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while updating category by {User}. Input={@Dto}", _currentUserService.GetCurrentUserName(), dto);
            throw;
        }
    }

    #endregion
}