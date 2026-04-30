using Application.Common.Specifications;
using Application.Entities;
using Application.Interfaces;
using AutoMapper;
using BusinessLogic.DTOs.ProductSubcategory;
using BusinessLogic.DTOs.User;
using BusinessLogic.Services.Interfaces;
using BusinessLogic.Specifications.ProductSubcategories;
using BusinessLogic.Specifications.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static Dapper.SqlMapper;

namespace BusinessLogic.Services.Implementations
{
    public class ProductSubcategoryService : IProductSubcategoryService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;
        private readonly ICurrentUserService _currentUserService;
        private readonly ILogger<ProductSubcategoryService> _logger;

        public ProductSubcategoryService(IUnitOfWork unitOfWork, IMapper mapper, ICurrentUserService currentUserService,
            ILogger<ProductSubcategoryService> logger)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
            _currentUserService = currentUserService;
            _logger = logger;
        }

        public async Task<ProductSubcategoryDto> CreateAsync(CreateProductSubcategoryDto dto, CancellationToken cancellationToken = default)
        {
            try
            {
                var Subcategory = _mapper.Map<ProductSubcategory>(dto);
                Subcategory.CreatedOn = DateTime.Now;
                Subcategory.CreatedById = _currentUserService.GetCurrentUserId();

                await _unitOfWork.Repository<ProductSubcategory>().AddAsync(Subcategory, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                _logger.LogInformation("زیردسته‌بندی با Id={Id} و Name={Name} ایجاد شد.", Subcategory.SubcategoryId, Subcategory.SubcategoryName);
                return _mapper.Map<ProductSubcategoryDto>(Subcategory);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطا هنگام ایجاد زیردسته‌بندی");
                throw;
            }
        }

        public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
        {
            try
            {
                var Subcategory = await _unitOfWork.Repository<ProductSubcategory>().GetByIdAsync(id, cancellationToken);
                if (Subcategory == null || !Subcategory.IsActive)
                {
                    _logger.LogWarning("⚠️ Delete failed. Subcategory with Id={Id} not found.", id);
                    return false;
                }
                 
                _unitOfWork.Repository<ProductSubcategory>().Delete(Subcategory);
                await _unitOfWork.SaveChangesAsync();

                _logger.LogInformation("🗑 Subcategory deleted successfully. name={name}", Subcategory.SubcategoryName);
                return true;

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error while deleting Subcategory. Id={Id}", id);
                throw;
            }
        }

        public async Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default)
        {
            try
            {
                var subcategory = await _unitOfWork.Repository<ProductSubcategory>().GetByIdAsync(id, cancellationToken);
                if (subcategory == null || !subcategory.IsActive)
                {
                    _logger.LogWarning("Subcategory with Id={Id} not found or inactive.", id);
                    return false;
                }

                _logger.LogInformation("Subcategory found. Id={Id}, Name={Name}",
                    subcategory.SubcategoryId, subcategory.SubcategoryName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error while checking existence of Subcategory with Id={Id}", id);
                throw;
            }
        }

        public async Task<IEnumerable<ProductSubcategoryDto>> GetAllAsync(string? search = null, int? includeCategoryId = null, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Retrieving all ProductSubcategories with search: {Search}", search ?? "<none>");

            try
            {
                var filter = string.IsNullOrWhiteSpace(search) ? null : $"SubcategoryName contains '{search}'";

                var spec = new QuerySpecification<ProductSubcategory, ProductSubcategoryDto>(
                    filter: filter,
                    sort: null,
                    skip: null,
                    take: null,
                    projection: includeCategoryId != null ? ProductSubcategoryQueryConfig.Projection : ProductSubcategoryQueryConfig.SimpleProjection,
                    allowedFields: ProductSubcategoryQueryConfig.AllowedFields,
                    applyDefaultSoftDelete: true
                );

                var productSubcategories = await _unitOfWork
                    .Repository<ProductSubcategory>()
                    .ListAsync<ProductSubcategoryDto>(spec, cancellationToken);

                _logger.LogDebug("Retrieved {Count} productSubcategories", productSubcategories.Count);

                return productSubcategories;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving all productSubcategories");
                throw;
            }
        }

        public async Task<ProductSubcategoryDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("Retrieving ProductSubcategory by ID: {ProductSubcategoryId},", id);

            try
            {
                var spec = new QuerySpecification<ProductSubcategory, ProductSubcategoryDto>(
                    filter: $"id eq {id}",
                    sort: null,
                    skip: null,
                    take: null,
                    projection: ProductSubcategoryQueryConfig.Projection,
                    allowedFields: ProductSubcategoryQueryConfig.AllowedFields,
                    applyDefaultSoftDelete: true
                );

                var productSubcategoryDto = await _unitOfWork
                    .Repository<ProductSubcategory>()
                    .FirstOrDefaultAsync<ProductSubcategoryDto>(spec, cancellationToken);

                if (productSubcategoryDto is null)
                {
                    _logger.LogDebug("ProductSubcategory not found with ID: {ProductSubcategoryId}", id);
                    return null;
                }

                return productSubcategoryDto;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving ProductSubcategory with ID: {ProductSubcategoryId}", id);
                throw;
            }
        }

        public async Task<int> GetCountByCategoryIdAsync(int categoryId, CancellationToken cancellationToken = default)
        {

            try
            {
                var spec = new ProductSubcategoryQueryCountSpecification(categoryId);

                var count = await _unitOfWork.Repository<ProductSubcategory>()
                    .CountAsync(spec, cancellationToken);

                _logger.LogInformation("Get count={count} Subcategory with CategoryId={CategoryId}", count, categoryId);
                return count;

            }
            catch(Exception ex)
            {
                _logger.LogError(ex, "❌ Error while getting count of Subcategories for CategoryId={CategoryId}", categoryId);
                throw;
            }
        }

        public async Task<ProductSubcategoryDto> UpdateAsync(UpdateProductSubcategoryDto dto)
        {
            try
            {
                var Subcategory = await _unitOfWork.Repository<ProductSubcategory>().GetByIdAsync(dto.ProductSubcategoryId);
                if (Subcategory == null || !Subcategory.IsActive)
                {
                    _logger.LogWarning("⚠️ Update failed. Subcategory with Id={Id} not found.", dto.ProductSubcategoryId);
                    throw new KeyNotFoundException($"زیردسته‌بندی با شناسه {dto.ProductSubcategoryId} یافت نشد");
                }

                _mapper.Map(dto, Subcategory);

                _unitOfWork.Repository<ProductSubcategory>().Update(Subcategory);
                await _unitOfWork.SaveChangesAsync();

                _logger.LogInformation("زیردسته‌بندی با Id={Id} به‌روزرسانی شد.", Subcategory.SubcategoryId);
                return _mapper.Map<ProductSubcategoryDto>(Subcategory);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطا هنگام به‌روزرسانی زیردسته‌بندی با Id={Id}", dto.ProductSubcategoryId);
                throw;
            }
        }
    }
}
