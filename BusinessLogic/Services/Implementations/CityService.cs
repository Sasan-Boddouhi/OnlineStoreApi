using Application.Entities;
using Application.Interfaces;
using AutoMapper;
using BusinessLogic.DTOs.City;
using BusinessLogic.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BusinessLogic.Services.Implementations
{
    public class CityService : ICityService
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

                await _unitOfWork.Repository<City>().DeleteAsync(city!);
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

        public async Task<bool> ExistsAsync(int id)
        {
            try
            {
                var city = await _unitOfWork.City.GetByIdAsync(id);
                if (city == null)
                {
                    _logger.LogWarning("City with Id={Id} not found.", id);
                    return false;
                }

                _logger.LogInformation("City found. Id={Id}, Name={Name}", city.CityId, city.CityName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while checking existence of City with Id={Id}", id);
                throw;
            }
        }

        public async Task<IEnumerable<CityDto>> GetAllAsync()
        {
            try
            {
                var cities = await _unitOfWork.City.Query()
                    .OrderBy(c => c.CityName)
                    .ToListAsync();

                _logger.LogInformation("Retrieved {Count} cities.", cities.Count);
                return _mapper.Map<IEnumerable<CityDto>>(cities);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while retrieving all cities");
                throw;
            }
        }

        public async Task<IEnumerable<CityDto>> GetAllByProvinceIdAsync(int provinceId)
        {
            try
            {
                var cities = await _unitOfWork.City.Query()
                    .Where(c => c.ProvinceId == provinceId)
                    .OrderBy(c => c.CityName)
                    .ToListAsync();

                _logger.LogInformation("Retrieved {Count} cities for ProvinceId={ProvinceId}", cities.Count, provinceId);
                return _mapper.Map<IEnumerable<CityDto>>(cities);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while retrieving cities for ProvinceId={ProvinceId}", provinceId);
                throw;
            }
        }

        public async Task<CityDto?> GetByIdAsync(int id)
        {
            try
            {
                var city = await _unitOfWork.City.Query()
                    .Where(c => c.CityId == id)
                    .FirstOrDefaultAsync();

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

        public async Task<CityDto?> GetByNameAsync(string name)
        {
            try
            {
                var city = await _unitOfWork.City.FirstOrDefaultAsync(c => c.CityName == name);

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

        public async Task<CityDto> UpdateAsync(UpdateCityDto dto)
        {
            try
            {
                var city = await _unitOfWork.City.GetByIdAsync(dto.CityId);
                if (city == null)
                {
                    _logger.LogWarning("Update failed. City with Id={Id} not found.", dto.CityId);
                    throw new KeyNotFoundException($"شهر با شناسه {dto.CityId} یافت نشد");
                }

                _mapper.Map(dto, city);

                await _unitOfWork.City.UpdateAsync(city);
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
    }
}
