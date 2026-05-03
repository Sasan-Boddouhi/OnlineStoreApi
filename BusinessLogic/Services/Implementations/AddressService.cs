using Application.Entities;
using Application.Interfaces;
using Application.Common.Specifications;
using AutoMapper;
using BusinessLogic.DTOs.Address;
using BusinessLogic.Services.Interfaces;
using Microsoft.Extensions.Logging;

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
        await _unitOfWork.BeginTransactionAsync();

        try
        {
            var address = _mapper.Map<Address>(dto);
            address.UserId = userId;

            if (dto.IsDefault)
            {
                await EnsureSingleDefaultAsync(userId);
            }
            else
            {
                var hasAnyAddress = await _unitOfWork
                    .Repository<Address>()
                    .AnyAsync(a => a.UserId == userId);

                if (!hasAnyAddress)
                    address.IsDefault = true;
            }

            await _unitOfWork.Repository<Address>().AddAsync(address);
            await _unitOfWork.SaveChangesAsync();
            await _unitOfWork.CommitTransactionAsync();

            return _mapper.Map<AddressDto>(address);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync();
            throw;
        }
    }

    private async Task EnsureSingleDefaultAsync(int userId)
    {
        var spec = new Spec<Address>()
            .Where(a => a.UserId == userId && a.IsDefault)
            .AsTracking();                     // نیاز به ویرایش

        var defaultAddresses = await _unitOfWork
            .Repository<Address>()
            .ListAsync(spec);

        foreach (var address in defaultAddresses)
        {
            address.IsDefault = false;
        }

        if (defaultAddresses.Any())
            _unitOfWork.Repository<Address>().UpdateRange(defaultAddresses);
    }
}