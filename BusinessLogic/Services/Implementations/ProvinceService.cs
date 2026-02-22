using Application.Entities;
using Application.Interfaces;
using AutoMapper;
using BusinessLogic.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Dapper.SqlMapper;

namespace BusinessLogic.Services.Implementations
{
    public class ProvinceService : IProvinceService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<ProvinceService> _logger;
        private readonly ICurrentUserService _currentUserService;
        private readonly IMapper _mapper;
        public ProvinceService(IUnitOfWork unitOfWork, ILogger<ProvinceService> logger, ICurrentUserService currentUserService, IMapper mapper)
        {
            _unitOfWork = unitOfWork;
            _logger = logger;
            _currentUserService = currentUserService;
            _mapper = mapper;

        }

        //public async Task<ProvinceDto> CreateAsync(CreateProvinceDto dto)
        //{
        //    try
        //    {
        //        var province = _mapper.Map<Province>(dto);

        //        await _unitOfWork.Province.AddAsync(province);
        //        await _unitOfWork.SaveChangesAsync();

        //        _logger.LogInformation("استان با Id={Id} و Name={Name} ایجاد شد.", province.ProvinceId, province.ProvinceName);
        //        return _mapper.Map<ProvinceDto>(province);
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError(ex, "خطا هنگام ایجاد استان");
        //        throw;
        //    }
        //}

        //public async Task<IEnumerable<ProvinceDto>> GetAllAsync()
        //{
        //    try
        //    {
        //        var provinces = await _unitOfWork.Province.GetAllAsync();
        //        var countOfProvince = provinces.Count();

        //        _logger.LogInformation("{Count} provinces fetched by {User} at {Time}.",
        //            countOfProvince, _currentUserService.GetCurrentUserName(), DateTime.Now);

        //        return _mapper.Map<IEnumerable<ProvinceDto>>(provinces);
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError(ex, "Error while fetching provinces by {User} at {Time}.",
        //            _currentUserService.GetCurrentUserName(), DateTime.Now);
        //        throw;
        //    }
        //}

        //public async Task<ProvinceDto?> GetByNameAsync(string provinceName)
        //{
        //    try
        //    {
        //        var province = await _unitOfWork.Province.Query()
        //            .FirstOrDefaultAsync(p => p.ProvinceName == provinceName);

        //        _logger.LogInformation("Province fetched by {User} at {Time}. {@Province}",
        //            _currentUserService.GetCurrentUserName(), DateTime.Now, province);
        //        return _mapper.Map<ProvinceDto>(province);
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError(ex, "Error while fetching Province ProvinceName={ProvinceName} by {User} at {Time}",
        //            provinceName, _currentUserService.GetCurrentUserName(), DateTime.Now);
        //        throw;
        //    }
        //}
    }
}
