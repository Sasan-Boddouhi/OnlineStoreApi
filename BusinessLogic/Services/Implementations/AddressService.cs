using Application.Entities;
using Application.Interfaces;
using Application.Common.Specifications;
using BusinessLogic.DTOs.Shared;
using BusinessLogic.DTOs.User;
using BusinessLogic.Services.Interfaces;
using BusinessLogic.Specifications.Users;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
using Application.Exceptions;
using Application.Interfaces.Security;
using System.Linq;
using AutoMapper;
using BusinessLogic.DTOs.Address;
using BusinessLogic.Specifications.Addresses;

namespace BusinessLogic.Services.Implementations;

public sealed class AddressService : IAddressService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<AddressService> _logger;
    private readonly IMemoryCache _cache;
    private readonly IMapper _mapper;

    private static readonly MemoryCacheEntryOptions _cacheEntryOptions = new()
    {
        SlidingExpiration = TimeSpan.FromMinutes(5),
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30),
        Priority = CacheItemPriority.Normal
    };

    private const string USER_FULL_CACHE_KEY_PREFIX = "UserFull_";
    private const string ALL_USERS_FULL_CACHE_KEY = "AllUsersFull";

    public AddressService(
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        ICurrentUserService currentUserService,
        ILogger<AddressService> logger,
        IMemoryCache cache,
        IMapper mapper)
    {
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _currentUserService = currentUserService;
        _logger = logger;
        _cache = cache;
        _mapper = mapper;
    }

    #region Create

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

    #endregion

    private async Task EnsureSingleDefaultAsync(int userId)
    {
        var spec = new DefaultAddressesByUserSpec(userId);

        var defaultAddresses = await _unitOfWork
            .Repository<Address>()
            .ListAsync(spec);

        foreach (var address in defaultAddresses)
        {
            address.IsDefault = false;
        }

        _unitOfWork.Repository<Address>().UpdateRange(defaultAddresses);
    }
}