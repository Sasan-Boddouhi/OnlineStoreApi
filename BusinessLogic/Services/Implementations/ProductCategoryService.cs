using Application.Entities;
using Application.Interfaces;
using AutoMapper;
using BusinessLogic.DTOs.ProductCategory;
using BusinessLogic.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BusinessLogic.Services.Implementations
{
    public class ProductCategoryService : IProductCategoryService
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

        public async Task<ProductCategoryDto> CreateAsync(CreateProductCategoryDto dto)
        {
            try
            {
                EnsureAdmin();

                var existingCategory = await _unitOfWork.ProductCategory.Query()
                    .FirstOrDefaultAsync(pc => pc.CategoryName == dto.Name);

                if (existingCategory != null)
                {
                    throw new Exception("Category with this name already exists.");
                }

                var entity = _mapper.Map<ProductCategory>(dto);

                await _unitOfWork.ProductCategory.AddAsync(entity);
                await _unitOfWork.SaveChangesAsync();

                _logger.LogInformation("Category created by {User} at {Time}. Id={Id}, Name={Name}, Entity={@Entity}",
                    _currentUserService.GetCurrentUserName(), DateTime.Now, entity.CategoryId, entity.CategoryName, entity);

                return _mapper.Map<ProductCategoryDto>(entity);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while creating category by {User} at {Time}. Input={@Dto}",
                    _currentUserService.GetCurrentUserName(), DateTime.Now, dto
                );
                throw;
            }
        }


        public async Task<bool> DeleteAsync(int id)
        {
            try
            {
                EnsureAdmin();

                var entity = await _unitOfWork.ProductCategory.GetByIdAsync(id);
                if (entity == null || !entity.IsActive)
                {
                    _logger.LogWarning("Delete failed. Category with Id={Id} not found or inactive. User={User}, Time={Time}",
                        id, _currentUserService.GetCurrentUserName(), DateTime.Now);
                    return false;
                }

                var oldEntity = _mapper.Map<ProductCategoryDto>(entity);

                await _unitOfWork.ProductCategory.DeleteAsync(entity);
                await _unitOfWork.SaveChangesAsync();

                _logger.LogInformation("Category deleted successfully by {User} at {Time}. Deleted entity: {@OldEntity}",
                    _currentUserService.GetCurrentUserName(), DateTime.Now, oldEntity);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while deleting category. Id={Id}, User={User}, Time={Time}",
                    id, _currentUserService.GetCurrentUserName(), DateTime.Now);
                throw;
            }
        }


        public async Task<IEnumerable<ProductCategoryDto>> GetAllAsync()
        {
            try
            {
                var categories = await _unitOfWork.ProductCategory.Query()
                    .Include(c => c.Subcategories)
                    .ToListAsync();

                _logger.LogInformation("{Count} categories fetched by {User} at {Time}.",
                    categories.Count, _currentUserService.GetCurrentUserName(), DateTime.Now);

                return _mapper.Map<IEnumerable<ProductCategoryDto>>(categories);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while fetching categories by {User} at {Time}.",
                    _currentUserService.GetCurrentUserName(), DateTime.Now);
                throw;
            }
        }


        public async Task<ProductCategoryDto?> GetByIdAsync(int id)
        {
            try
            {
                var entity = await _unitOfWork.ProductCategory.GetByIdAsync(id);
                if (entity == null)
                {
                    _logger.LogWarning("Category with Id={Id} not found. Requested by {User} at {Time}.",
                        id, _currentUserService.GetCurrentUserName(), DateTime.Now);
                    return null;
                }

                _logger.LogInformation("Category fetched by {User} at {Time}. {@Category}",
                    _currentUserService.GetCurrentUserName(), DateTime.Now, entity);
                return _mapper.Map<ProductCategoryDto>(entity);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while fetching category Id={Id} by {User} at {Time}",
                    id, _currentUserService.GetCurrentUserName(), DateTime.Now);
                throw;
            }
        }


        public async Task<ProductCategoryDto?> UpdateAsync(UpdateProductCategoryDto dto)
        {
            try
            {
                EnsureAdmin();

                var existingCategory = await _unitOfWork.ProductCategory.Query()
                    .FirstOrDefaultAsync(pc => pc.CategoryName == dto.Name && pc.CategoryId != dto.ProductCategoryId);

                if (existingCategory != null)
                {
                    throw new Exception("Category with this name already exists.");
                }

                var entity = await _unitOfWork.ProductCategory.GetByIdAsync(dto.ProductCategoryId);
                if (entity == null || !entity.IsActive)
                {
                    _logger.LogWarning("Update failed. Category with Id={Id} not found.", dto.ProductCategoryId);
                    return null;
                }

                var oldEntity = _mapper.Map<ProductCategoryDto>(entity);

                _mapper.Map(dto, entity);

                await _unitOfWork.ProductCategory.UpdateAsync(entity);
                await _unitOfWork.SaveChangesAsync();

                var newEntity = _mapper.Map<ProductCategoryDto>(entity);

                _logger.LogInformation("Category updated by {User} at {Time}. Old: {@OldEntity}, New: {@NewEntity}",
                    _currentUserService.GetCurrentUserName(), DateTime.Now, oldEntity, newEntity);
                return _mapper.Map<ProductCategoryDto>(entity);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while updating category by {User}. Input={@Dto}", _currentUserService.GetCurrentUserName(), dto);
                throw;
            }
        }
    }
}
