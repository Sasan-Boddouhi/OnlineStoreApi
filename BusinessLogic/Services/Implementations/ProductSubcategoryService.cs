using Application.Entities;
using Application.Interfaces;
using AutoMapper;
using BusinessLogic.DTOs.ProductSubcategory;
using BusinessLogic.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

        public async Task<ProductSubcategoryDto> CreateAsync(CreateProductSubcategoryDto dto)
        {
            try
            {
                var Subcategory = _mapper.Map<ProductSubcategory>(dto);
                Subcategory.CreatedOn = DateTime.Now;
                Subcategory.CreatedById = _currentUserService.GetCurrentUserId();

                await _unitOfWork.ProductSubcategory.AddAsync(Subcategory);
                await _unitOfWork.SaveChangesAsync();

                _logger.LogInformation("زیردسته‌بندی با Id={Id} و Name={Name} ایجاد شد.", Subcategory.SubcategoryId, Subcategory.SubcategoryName);
                return _mapper.Map<ProductSubcategoryDto>(Subcategory);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطا هنگام ایجاد زیردسته‌بندی");
                throw;
            }
        }

        public async Task<bool> DeleteAsync(int id)
        {
            try
            {
                var Subcategory = await _unitOfWork.ProductSubcategory.GetByIdAsync(id);
                if (Subcategory == null || !Subcategory.IsActive)
                {
                    _logger.LogWarning("⚠️ Delete failed. Subcategory with Id={Id} not found.", id);
                    return false;
                }
                 
                await _unitOfWork.ProductSubcategory.DeleteAsync(Subcategory!);
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

        public async Task<bool> ExistsAsync(int id)
        {
            try
            {
                var subcategory = await _unitOfWork.ProductSubcategory.GetByIdAsync(id);
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

        public async Task<IEnumerable<ProductSubcategoryDto>> GetAllAsync()
        {
            try
            {
                var productSubcategories = await _unitOfWork.ProductSubcategory.Query()
                    .Where(ps => ps.IsActive)
                    .Include(ps => ps.Products)
                    .OrderBy(ps => ps.SubcategoryName)
                    .ToListAsync();

                _logger.LogInformation("Retrieved {Count} active subcategories.", productSubcategories.Count);
                return _mapper.Map<IEnumerable<ProductSubcategoryDto>>(productSubcategories);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error while retrieving all subcategories");
                throw;
            }
        }

        public async Task<IEnumerable<ProductSubcategoryDto>> GetAllByCategoryIdAsync(int categoryId)
        {
            try
            {
                var productSubcategories = await _unitOfWork.ProductSubcategory.Query()
                    .Where(ps => ps.CategoryId == categoryId && ps.IsActive)
                    .OrderBy(ps => ps.SubcategoryName) // اضافه کردن مرتب‌سازی
                    .ToListAsync();

                _logger.LogInformation("Retrieved {Count} subcategories for CategoryId={CategoryId}",
                    productSubcategories.Count, categoryId);
                return _mapper.Map<IEnumerable<ProductSubcategoryDto>>(productSubcategories);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error while retrieving subcategories for CategoryId={CategoryId}", categoryId);
                throw;
            }
        }

        public async Task<ProductSubcategoryDto?> GetByIdAsync(int id)
        {
            try
            {
                var productSubcategory = await _unitOfWork.ProductSubcategory.Query()
                    .Where(ps => ps.SubcategoryId == id && ps.IsActive)
                    .Include(ps => ps.Products)
                    .FirstOrDefaultAsync();

                if (productSubcategory == null)
                {
                    _logger.LogWarning("Subcategory with Id={Id} not found or inactive.", id);
                    return null;
                }

                _logger.LogInformation("Subcategory with Id={Id} retrieved successfully.", id);
                return _mapper.Map<ProductSubcategoryDto>(productSubcategory);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error while retrieving Subcategory with Id={Id}", id);
                throw;
            }
        }

        public async Task<int> GetCountByCategoryIdAsync(int categoryId)
        {

            try
            {
                var count = await _unitOfWork.ProductSubcategory.Query()
                    .CountAsync(ps => ps.CategoryId == categoryId && ps.IsActive);

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
                var Subcategory = await _unitOfWork.ProductSubcategory.GetByIdAsync(dto.ProductSubcategoryId);
                if (Subcategory == null || !Subcategory.IsActive)
                {
                    _logger.LogWarning("⚠️ Update failed. Subcategory with Id={Id} not found.", dto.ProductSubcategoryId);
                    throw new KeyNotFoundException($"زیردسته‌بندی با شناسه {dto.ProductSubcategoryId} یافت نشد");
                }

                _mapper.Map(dto, Subcategory);

                await _unitOfWork.ProductSubcategory.UpdateAsync(Subcategory);
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
