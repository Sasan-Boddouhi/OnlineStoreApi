using Application.Entities;
using Application.Interfaces;
using Application.Common.Specifications;
using AutoMapper;
using BusinessLogic.Services.Interfaces;
using Microsoft.Extensions.Logging;
using BusinessLogic.DTOs.Address;

namespace BusinessLogic.Services.Implementations;

public sealed class AddressService : IAddressService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<AddressService> _logger;
    private readonly IMapper _mapper;

    public AddressService(
        IUnitOfWork unitOfWork,
        ILogger<AddressService> logger,
        IMapper mapper)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
        _mapper = mapper;
    }

    public async Task<AddressDto> CreateAsync(int userId, CreateAddressDto dto)
    {
        var address = _mapper.Map<Address>(dto);

        address.UserId = userId;

        if (dto.IsDefault)
        {
            await ResetDefaultAddresses(userId);
        }
        else
        {
            var hasAny = await _unitOfWork.Repository<Address>()
                .AnyAsync(a => a.UserId == userId);

            if (!hasAny)
                address.IsDefault = true;
        }

        await _unitOfWork.Repository<Address>().AddAsync(address);
        await _unitOfWork.SaveChangesAsync();

        return _mapper.Map<AddressDto>(address);
    }

    private async Task ResetDefaultAddresses(int userId)
    {
        var spec = new Spec<Address>()
            .Where(a => a.UserId == userId && a.IsDefault)
            .AsTracking();

        var addresses = await _unitOfWork.Repository<Address>()
            .ListAsync(spec);

        foreach (var item in addresses)
        {
            item.IsDefault = false;
        }
    }
}