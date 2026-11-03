using Application.Entities;
using Application.Interfaces;
using Application.Interfaces.Services;
using Application.Common.Specifications;
using Application.Exceptions;
using AutoMapper;
using BusinessLogic.DTOs.ProductCategory;
using BusinessLogic.DTOs.ProductSubcategory;
using BusinessLogic.Services.Interfaces;
using Microsoft.Extensions.Logging;
using BusinessLogic.Specifications.ProductCategories;

namespace BusinessLogic.Services.Implementations;

public sealed class ProductCategoryService : IProductCategoryService
{
    private const string AllCategoriesCacheKey = "categories:all";
    private const string CategoryTreeCacheKey = "categories:tree";

    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<ProductCategoryService> _logger;
    private readonly ICacheService? _cacheService;

    public ProductCategoryService(
        IUnitOfWork unitOfWork,
        IMapper mapper,
        ICurrentUserService currentUserService,
        ILogger<ProductCategoryService> logger,
        ICacheService? cacheService = null)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
        _currentUserService = currentUserService;
        _logger = logger;
        _cacheService = cacheService;
    }

    #region Create

    public async Task<ProductCategoryDto> CreateAsync(CreateProductCategoryDto dto, CancellationToken cancellationToken = default)
    {
        try
        {
            var exists = await _unitOfWork.Repository<ProductCategory>()
                .AnyAsync(pc => pc.CategoryName == dto.Name, cancellationToken);
            if (exists)
                throw new BusinessException("دسته‌بندی با این نام قبلاً وجود دارد.", "CATEGORY_NAME_DUPLICATE");

            var entity = _mapper.Map<ProductCategory>(dto);
            await _unitOfWork.Repository<ProductCategory>().AddAsync(entity, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            if (_cacheService != null)
            {
                await _cacheService.RemoveAsync(AllCategoriesCacheKey, cancellationToken);
                await _cacheService.RemoveAsync(CategoryTreeCacheKey, cancellationToken);
            }

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
            var entity = await _unitOfWork.Repository<ProductCategory>().GetByIdAsync(id, cancellationToken);
            if (entity == null || !entity.IsActive)
            {
                _logger.LogWarning("Delete failed. Category with Id={Id} not found or inactive. User={User}, Time={Time}",
                    id, _currentUserService.GetCurrentUserName(), DateTime.Now);
                return false;
            }

            var subcategoryRepo = _unitOfWork.Repository<ProductSubcategory>();
            if (subcategoryRepo != null && await subcategoryRepo.AnyAsync(sc => sc.CategoryId == id, cancellationToken))
                throw new BusinessException("این دسته‌بندی دارای زیردسته‌بندی است و قابل حذف نیست.", "CATEGORY_HAS_SUBCATEGORIES");

            _unitOfWork.Repository<ProductCategory>().Delete(entity);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            if (_cacheService != null)
            {
                await _cacheService.RemoveAsync(AllCategoriesCacheKey, cancellationToken);
                await _cacheService.RemoveAsync(CategoryTreeCacheKey, cancellationToken);
                await _cacheService.RemoveAsync($"categories:{id}", cancellationToken);
            }

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

    #region Get All

    public async Task<IEnumerable<ProductCategoryDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        if (_cacheService != null)
        {
            var cached = await _cacheService.GetAsync<List<ProductCategoryDto>>(AllCategoriesCacheKey, cancellationToken);
            if (cached != null)
                return cached;
        }

        try
        {
            var spec = new Spec<ProductCategory>()
                .Where(pc => pc.IsActive)
                .OrderBy(pc => pc.CategoryName);

            var categories = await _unitOfWork.Repository<ProductCategory>()
                .ListAsync(spec, ProductCategoryQueryConfig.Projection, cancellationToken);

            var list = categories.ToList();

            if (_cacheService != null)
                await _cacheService.SetAsync(AllCategoriesCacheKey, list, TimeSpan.FromHours(1), cancellationToken);

            _logger.LogInformation("{Count} categories fetched by {User} at {Time}.",
                categories.Count, _currentUserService.GetCurrentUserName(), DateTime.Now);

            return list;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while fetching categories by {User} at {Time}.",
                _currentUserService.GetCurrentUserName(), DateTime.Now);
            throw;
        }
    }

    public async Task<IReadOnlyList<CategoryTreeDto>> GetTreeAsync(CancellationToken cancellationToken = default)
    {
        if (_cacheService != null)
        {
            var cached = await _cacheService.GetAsync<List<CategoryTreeDto>>(CategoryTreeCacheKey, cancellationToken);
            if (cached != null)
                return cached;
        }

        try
        {
            var spec = new Spec<ProductCategory>()
                .Where(pc => pc.IsActive)
                .Include(pc => pc.Subcategories)
                .OrderBy(pc => pc.CategoryName);

            var categories = await _unitOfWork.Repository<ProductCategory>()
                .ListAsync(spec, cancellationToken);

            var tree = categories
                .Select(c => new CategoryTreeDto
                {
                    CategoryId = c.CategoryId,
                    Name = c.CategoryName,
                    IsActive = c.IsActive,
                    Subcategories = c.Subcategories
                        .Where(s => s.IsActive)
                        .OrderBy(s => s.SubcategoryName)
                        .Select(s => new ProductSubcategoryDto
                        {
                            SubcategoryId = s.SubcategoryId,
                            SubcategoryName = s.SubcategoryName,
                            CategoryId = s.CategoryId,
                            CategoryName = c.CategoryName,
                            IsActive = s.IsActive,
                            Description = s.Description
                        })
                        .ToList()
                })
                .ToList();

            if (_cacheService != null)
                await _cacheService.SetAsync(CategoryTreeCacheKey, tree, TimeSpan.FromHours(1), cancellationToken);

            return tree;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while fetching category tree by {User} at {Time}.",
                _currentUserService.GetCurrentUserName(), DateTime.Now);
            throw;
        }
    }

    #endregion

    #region Get By Id

    public async Task<ProductCategoryDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"categories:{id}";
        if (_cacheService != null)
        {
            var cached = await _cacheService.GetAsync<ProductCategoryDto>(cacheKey, cancellationToken);
            if (cached != null)
                return cached;
        }

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
            {
                if (_cacheService != null)
                    await _cacheService.SetAsync(cacheKey, dto, TimeSpan.FromHours(1), cancellationToken);

                _logger.LogInformation("Category fetched by {User} at {Time}. Id={Id}",
                    _currentUserService.GetCurrentUserName(), DateTime.Now, id);
            }

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
            var exists = await _unitOfWork.Repository<ProductCategory>()
                .AnyAsync(pc => pc.CategoryName == dto.Name && pc.CategoryId != dto.ProductCategoryId, cancellationToken);
            if (exists)
                throw new BusinessException("دسته‌بندی با این نام قبلاً وجود دارد.", "CATEGORY_NAME_DUPLICATE");

            var entity = await _unitOfWork.Repository<ProductCategory>().GetByIdAsync(dto.ProductCategoryId, cancellationToken);
            if (entity == null || !entity.IsActive)
            {
                _logger.LogWarning("Update failed. Category with Id={Id} not found or inactive.", dto.ProductCategoryId);
                return null;
            }

            _mapper.Map(dto, entity);
            _unitOfWork.Repository<ProductCategory>().Update(entity);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            if (_cacheService != null)
            {
                await _cacheService.RemoveAsync(AllCategoriesCacheKey, cancellationToken);
                await _cacheService.RemoveAsync(CategoryTreeCacheKey, cancellationToken);
                await _cacheService.RemoveAsync($"categories:{entity.CategoryId}", cancellationToken);
            }

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