using Application.Entities;
using BusinessLogic.DTOs.Auth;
using BusinessLogic.DTOs.City;
using BusinessLogic.DTOs.Shared;
using BusinessLogic.DTOs.User;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusinessLogic.Services.Interfaces
{
    public interface ICityService
    {
        Task<PagedResult<CityDto>> GetByQueryAsync(
            QueryContract<City> query,
            CancellationToken cancellationToken = default);

        Task<CityDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
        Task<CityDto> CreateAsync(CreateCityDto dto, CancellationToken cancellationToken = default);
        Task<CityDto?> UpdateAsync(UpdateCityDto dto, CancellationToken cancellationToken = default);
        Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
    }
}
