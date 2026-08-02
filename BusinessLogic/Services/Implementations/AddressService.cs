using Application.Entities;
using Application.Interfaces;
using Application.Common.Specifications;
using Application.Exceptions;
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

    public async Task<AddressDto> CreateAsync(int userId, CreateAddressDto dto, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating address for user {UserId}", userId);

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
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

            await _unitOfWork.Repository<Address>().AddAsync(address, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            _logger.LogInformation("Address created with ID: {AddressId}", address.AddressId);

            return _mapper.Map<AddressDto>(address);
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            _logger.LogError(ex, "Failed to create address for user {UserId}", userId);
            throw new BusinessException("خطا در ایجاد آدرس", "ADDRESS_CREATE_ERROR");
        }
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