using Application.Entities;
using Application.Exceptions;
using Application.Helper;
using Application.Interfaces;
using Application.Interfaces.Security;
using Application.Interfaces.Services;
using AutoMapper;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Features.Users.CreateUser;

public sealed class CreateUserCommandHandler : IRequestHandler<CreateUserCommand, int>
{
    private const string ALL_USERS_FULL_CACHE_KEY = "AllUsersFull";

    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ICacheService _cacheService;
    private readonly IMapper _mapper;
    private readonly ILogger<CreateUserCommandHandler> _logger;

    public CreateUserCommandHandler(
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        ICacheService cacheService,
        IMapper mapper,
        ILogger<CreateUserCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _cacheService = cacheService;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<int> Handle(
        CreateUserCommand request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Creating new user with phone: {PhoneNumber}",
            request.PhoneNumber);

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            var exists = await _unitOfWork.Repository<User>()
                .AnyAsync(
                    x => x.PhoneNumber == request.PhoneNumber,
                    cancellationToken);

            if (exists)
            {
                throw new BusinessException(
                    "شماره موبایل قبلاً ثبت شده است.",
                    "USER_PHONE_EXISTS");
            }

            var user = _mapper.Map<User>(request);
            user.PasswordHash = _passwordHasher.Hash(request.Password);
            user.SecurityStamp = Guid.NewGuid().ToString();
            user.IsActive = true;

            if (!string.IsNullOrWhiteSpace(request.DateOfBirth))
            {
                user.DateOfBirth = PersianDateHelper.ToGregorian(request.DateOfBirth);
            }

            await _unitOfWork.Repository<User>()
                .AddAsync(user, cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            if (request.Addresses?.Any() == true)
            {
                var addresses = request.Addresses
                    .Where(a => !string.IsNullOrWhiteSpace(a.Plaque))
                    .Select(a =>
                    {
                        var address = _mapper.Map<Address>(a);
                        address.UserId = user.UserId;
                        return address;
                    })
                    .ToList();

                await _unitOfWork.Repository<Address>()
                    .AddRangeAsync(addresses, cancellationToken);

                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            _logger.LogInformation(
                "User created successfully with ID: {UserId}",
                user.UserId);

            await _cacheService.RemoveAsync(
                ALL_USERS_FULL_CACHE_KEY,
                cancellationToken);

            return user.UserId;
        }
        catch (Exception ex) when (ex is not BusinessException)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);

            _logger.LogError(
                ex,
                "Failed to create user with phone: {PhoneNumber}",
                request.PhoneNumber);

            throw;
        }
    }
}