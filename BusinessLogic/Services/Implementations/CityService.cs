using Application.Entities;
using Application.Interfaces;
using Application.Common.Specifications;
using AutoMapper;
using BusinessLogic.DTOs.City;
using BusinessLogic.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace BusinessLogic.Services.Implementations;

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

    public async Task<CityDto> CreateAsync(CreateCityDto dto)
    {
        var city = _mapper.Map<City>(dto);
        await _unitOfWork.Repository<City>().AddAsync(city);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("City created with Id={Id}, Name={Name}", city.CityId, city.CityName);
        return _mapper.Map<CityDto>(city);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var city = await _unitOfWork.Repository<City>().GetByIdAsync(id);
        if (city == null) return false;

        _unitOfWork.Repository<City>().Delete(city);
        await _unitOfWork.SaveChangesAsync();
        _logger.LogInformation("City deleted: {Name}", city.CityName);
        return true;
    }

    public async Task<bool> ExistsAsync(int id)
    {
        return await _unitOfWork.Repository<City>().AnyAsync(c => c.CityId == id);
    }

    public async Task<IEnumerable<CityDto>> GetAllAsync()
    {
        var spec = new Spec<City>()
            .OrderByFirst(c => c.CityName);    // مرتب‌سازی صعودی پیش‌فرض

        var cities = await _unitOfWork.Repository<City>().ListAsync(spec);
        return _mapper.Map<IEnumerable<CityDto>>(cities);
    }

    public async Task<IEnumerable<CityDto>> GetAllByProvinceIdAsync(int provinceId)
    {
        var spec = new Spec<City>()
            .Where(c => c.ProvinceId == provinceId)
            .OrderByFirst(c => c.CityName);

        var cities = await _unitOfWork.Repository<City>().ListAsync(spec);
        return _mapper.Map<IEnumerable<CityDto>>(cities);
    }

    public async Task<CityDto?> GetByIdAsync(int id)
    {
        var city = await _unitOfWork.Repository<City>().GetByIdAsync(id);
        return city == null ? null : _mapper.Map<CityDto>(city);
    }

    public async Task<CityDto?> GetByNameAsync(string name)
    {
        var spec = new Spec<City>()
            .Where(c => c.CityName == name);

        var city = await _unitOfWork.Repository<City>().FirstOrDefaultAsync(spec);
        return city == null ? null : _mapper.Map<CityDto>(city);
    }

    public async Task<CityDto> UpdateAsync(UpdateCityDto dto)
    {
        var city = await _unitOfWork.Repository<City>().GetByIdAsync(dto.CityId)
            ?? throw new KeyNotFoundException($"City with Id={dto.CityId} not found.");

        _mapper.Map(dto, city);
        _unitOfWork.Repository<City>().Update(city);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("City updated: {Id}", city.CityId);
        return _mapper.Map<CityDto>(city);
    }
}