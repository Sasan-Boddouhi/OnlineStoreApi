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
                await ResetDefaultAddresses(userId, cancellationToken);
            }
            else
            {
                var hasAny = await _unitOfWork.Repository<Address>()
                    .AnyAsync(a => a.UserId == userId, cancellationToken);

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

    public async Task<IReadOnlyList<AddressDto>> GetAllAsync(int userId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting all addresses for user {UserId}", userId);

        var spec = new Spec<Address>().Where(a => a.UserId == userId);
        var addresses = await _unitOfWork.Repository<Address>().ListAsync(spec, cancellationToken);

        return addresses.Select(a => _mapper.Map<AddressDto>(a)).ToList();
    }

    public async Task<AddressDto?> UpdateAsync(int userId, int addressId, UpdateAddressDto dto, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Updating address {AddressId} for user {UserId}", addressId, userId);

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var address = await _unitOfWork.Repository<Address>().GetByIdAsync(addressId, cancellationToken);
            if (address == null)
                return null;

            // Check ownership
            if (address.UserId != userId)
                throw new BusinessException("شما مجاز به ویرایش این آدرس نیستید.", "ADDRESS_FORBIDDEN");

            if (dto.IsDefault)
            {
                await ResetDefaultAddresses(userId, cancellationToken);
            }

            _mapper.Map(dto, address);

            _unitOfWork.Repository<Address>().Update(address);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            _logger.LogInformation("Address {AddressId} updated successfully", addressId);

            return _mapper.Map<AddressDto>(address);
        }
        catch (BusinessException)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            _logger.LogError(ex, "Failed to update address {AddressId} for user {UserId}", addressId, userId);
            throw new BusinessException("خطا در ویرایش آدرس", "ADDRESS_UPDATE_ERROR");
        }
    }

    public async Task<bool> DeleteAsync(int userId, int addressId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting address {AddressId} for user {UserId}", addressId, userId);

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var address = await _unitOfWork.Repository<Address>().GetByIdAsync(addressId, cancellationToken);
            if (address == null)
                return false;

            // Check ownership
            if (address.UserId != userId)
                throw new BusinessException("شما مجاز به حذف این آدرس نیستید.", "ADDRESS_FORBIDDEN");

            _unitOfWork.Repository<Address>().Delete(address);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            _logger.LogInformation("Address {AddressId} deleted successfully", addressId);

            return true;
        }
        catch (BusinessException)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            _logger.LogError(ex, "Failed to delete address {AddressId} for user {UserId}", addressId, userId);
            throw new BusinessException("خطا در حذف آدرس", "ADDRESS_DELETE_ERROR");
        }
    }

    private async Task ResetDefaultAddresses(int userId, CancellationToken cancellationToken)
    {
        var spec = new Spec<Address>()
            .Where(a => a.UserId == userId && a.IsDefault)
            .AsTracking();

        var addresses = await _unitOfWork.Repository<Address>()
            .ListAsync(spec, cancellationToken);

        foreach (var item in addresses)
        {
            item.IsDefault = false;
        }
    }
}