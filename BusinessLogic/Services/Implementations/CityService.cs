using Application.Entities;
using Application.Interfaces;
using Application.Common.Specifications;
using AutoMapper;
using BusinessLogic.DTOs.City;
using BusinessLogic.Services.Interfaces;
using Microsoft.Extensions.Logging;
using System.Linq.Expressions;
using BusinessLogic.Specifications.Cities;

namespace BusinessLogic.Services.Implementations
{
    public sealed class CityService : ICityService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;
        private readonly ILogger<CityService> _logger;

        public CityService(IUnitOfWork unitOfWork, IMapper mapper, ILogger<CityService> logger)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
            _logger = logger;
        }

        #region Create

        public async Task<CityDto> CreateAsync(CreateCityDto dto)
        {
            try
            {
                var city = _mapper.Map<City>(dto);

                await _unitOfWork.Repository<City>().AddAsync(city);
                await _unitOfWork.SaveChangesAsync();

                _logger.LogInformation("شهر با Id={Id} و Name={Name} ایجاد شد.", city.CityId, city.CityName);
                return _mapper.Map<CityDto>(city);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطا هنگام ایجاد شهر");
                throw;
            }
        }

        #endregion

        #region Delete

        public async Task<bool> DeleteAsync(int id)
        {
            try
            {
                var city = await _unitOfWork.Repository<City>().GetByIdAsync(id);
                if (city == null)
                {
                    _logger.LogWarning("Delete failed. City with Id={Id} not found.", id);
                    return false;
                }

                _unitOfWork.Repository<City>().Delete(city);
                await _unitOfWork.SaveChangesAsync();

                _logger.LogInformation("City deleted successfully. name={Name}", city.CityName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while deleting City. Id={Id}", id);
                throw;
            }
        }

        #endregion

        #region Exists

        public async Task<bool> ExistsAsync(int id)
        {
            try
            {
                var exists = await _unitOfWork.Repository<City>().AnyAsync(c => c.CityId == id);
                if (!exists)
                {
                    _logger.LogWarning("City with Id={Id} not found.", id);
                    return false;
                }

                _logger.LogInformation("City found with Id={Id}.", id);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while checking existence of City with Id={Id}", id);
                throw;
            }
        }

        #endregion

        #region Get All

        public async Task<IEnumerable<CityDto>> GetAllAsync()
        {
            try
            {
                var spec = new AllCitiesOrderedSpecification();
                var cities = await _unitOfWork.Repository<City>().ListAsync(spec);

                _logger.LogInformation("Retrieved {Count} cities.", cities.Count);
                return _mapper.Map<IEnumerable<CityDto>>(cities);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while retrieving all cities");
                throw;
            }
        }

        #endregion

        #region Get All By ProvinceId

        public async Task<IEnumerable<CityDto>> GetAllByProvinceIdAsync(int provinceId)
        {
            try
            {
                var spec = new CitiesByProvinceIdSpecification(provinceId);
                var cities = await _unitOfWork.Repository<City>().ListAsync(spec);

                _logger.LogInformation("Retrieved {Count} cities for ProvinceId={ProvinceId}", cities.Count, provinceId);
                return _mapper.Map<IEnumerable<CityDto>>(cities);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while retrieving cities for ProvinceId={ProvinceId}", provinceId);
                throw;
            }
        }

        #endregion

        #region Get By Id

        public async Task<CityDto?> GetByIdAsync(int id)
        {
            try
            {
                var city = await _unitOfWork.Repository<City>().GetByIdAsync(id);

                if (city == null)
                {
                    _logger.LogWarning("City with Id={Id} not found.", id);
                    return null;
                }

                _logger.LogInformation("City with Id={Id} retrieved successfully.", id);
                return _mapper.Map<CityDto>(city);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while retrieving City with Id={Id}", id);
                throw;
            }
        }

        #endregion

        #region Get By Name

        public async Task<CityDto?> GetByNameAsync(string name)
        {
            try
            {
                var spec = new CityByNameSpecification(name);
                var city = await _unitOfWork.Repository<City>().FirstOrDefaultAsync(spec);

                if (city == null)
                {
                    _logger.LogWarning("City with Name={Name} not found.", name);
                    return null;
                }

                _logger.LogInformation("City with Name={Name} retrieved successfully.", name);
                return _mapper.Map<CityDto>(city);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while retrieving City with Name={Name}", name);
                throw;
            }
        }

        private sealed class CityByNameSpecification : BaseSpecification<City>
        {
            public CityByNameSpecification(string name)
            {
                Criteria = c => c.CityName == name;
            }
        }

        #endregion

        #region Update

        public async Task<CityDto> UpdateAsync(UpdateCityDto dto)
        {
            try
            {
                var city = await _unitOfWork.Repository<City>().GetByIdAsync(dto.CityId);
                if (city == null)
                {
                    _logger.LogWarning("Update failed. City with Id={Id} not found.", dto.CityId);
                    throw new KeyNotFoundException($"شهر با شناسه {dto.CityId} یافت نشد");
                }

                _mapper.Map(dto, city);

                _unitOfWork.Repository<City>().Update(city);
                await _unitOfWork.SaveChangesAsync();

                _logger.LogInformation("شهر با Id={Id} به‌روزرسانی شد.", city.CityId);
                return _mapper.Map<CityDto>(city);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطا هنگام به‌روزرسانی شهر با Id={Id}", dto.CityId);
                throw;
            }
        }

        #endregion
    }
}