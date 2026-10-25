using System.Linq.Expressions;
using Application.Entities;
using Application.Exceptions;
using Application.Features.Users.CreateUser;
using Application.Interfaces;
using Application.Interfaces.Security;
using Application.Interfaces.Services;
using AutoMapper;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace OnlineStore.Tests.Unit.Features.Users;

public class CreateUserCommandHandlerTests
{
    private readonly Mock<IUnitOfWork> _uowMock = new();
    private readonly Mock<IPasswordHasher> _hasherMock = new();
    private readonly Mock<ICacheService> _cacheMock = new();
    private readonly Mock<IMapper> _mapperMock = new();
    private readonly Mock<ILogger<CreateUserCommandHandler>> _loggerMock = new();
    private readonly Mock<IGenericRepository<User>> _userRepoMock = new();
    private readonly Mock<IGenericRepository<Address>> _addressRepoMock = new();

    private readonly CreateUserCommandHandler _handler;

    public CreateUserCommandHandlerTests()
    {
        _uowMock
            .Setup(u => u.Repository<User>())
            .Returns(_userRepoMock.Object);

        _uowMock
            .Setup(u => u.Repository<Address>())
            .Returns(_addressRepoMock.Object);

        _uowMock
            .Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _uowMock
            .Setup(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _uowMock
            .Setup(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _uowMock
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _cacheMock
            .Setup(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _handler = new CreateUserCommandHandler(
            _uowMock.Object,
            _hasherMock.Object,
            _cacheMock.Object,
            _mapperMock.Object,
            _loggerMock.Object);
    }

    private static CreateUserCommand CreateCommand(
        string phone = "09120000000",
        IReadOnlyList<CreateUserAddressCommand>? addresses = null)
        => new(
            "Ali",
            "Rezaei",
            phone,
            "Test@123",
            "ali@example.com",
            "1370/01/01",
            addresses);

    private static User CreateUser(int id = 5)
        => new()
        {
            UserId = id,
            FirstName = "Ali",
            LastName = "Rezaei",
            PhoneNumber = "09120000000",
            PasswordHash = "hashed",
            UserType = UserType.Customer,
            SecurityStamp = Guid.NewGuid().ToString()
        };

    [Fact]
    public async Task Handle_ValidCommand_CreatesUserAndReturnsUserId()
    {
        var command = CreateCommand();
        var user = CreateUser();

        _userRepoMock
            .Setup(r => r.AnyAsync(
                It.IsAny<Expression<Func<User, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _mapperMock
            .Setup(m => m.Map<User>(command))
            .Returns(user);

        _hasherMock
            .Setup(h => h.Hash(command.Password))
            .Returns("hashed");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Should().Be(user.UserId);
        user.IsActive.Should().BeTrue();
        user.PasswordHash.Should().Be("hashed");
        user.DateOfBirth.Should().NotBeNull();

        _userRepoMock.Verify(
            r => r.AddAsync(user, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_DuplicatePhone_ThrowsBusinessException()
    {
        var command = CreateCommand();

        _userRepoMock
            .Setup(r => r.AnyAsync(
                It.IsAny<Expression<Func<User, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var act = () => _handler.Handle(command, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<BusinessException>();
        exception.Which.Code.Should().Be("USER_PHONE_EXISTS");

        _uowMock.Verify(
            u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ValidCommand_HashesPassword()
    {
        var command = CreateCommand();
        var user = CreateUser();

        _userRepoMock
            .Setup(r => r.AnyAsync(
                It.IsAny<Expression<Func<User, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _mapperMock
            .Setup(m => m.Map<User>(command))
            .Returns(user);

        _hasherMock
            .Setup(h => h.Hash(command.Password))
            .Returns("secure-hash");

        await _handler.Handle(command, CancellationToken.None);

        _hasherMock.Verify(h => h.Hash(command.Password), Times.Once);
        user.PasswordHash.Should().Be("secure-hash");
    }

    [Fact]
    public async Task Handle_CommandWithAddresses_PersistsValidAddresses()
    {
        var addresses = new[]
        {
            new CreateUserAddressCommand(
                1, "12", "1", "1234567890", "Ali", "Rezaei", null, true),
            new CreateUserAddressCommand(
                2, " ", "2", "0987654321", "Sara", "Rezaei", null, false)
        };
        var command = CreateCommand(addresses: addresses);
        var user = CreateUser(7);
        var mappedAddress = new Address
        {
            AddressId = 10,
            CityId = 1,
            Plaque = "12",
            Unit = "1",
            PostalCode = "1234567890",
            RecipientFirstName = "Ali",
            RecipientLastName = "Rezaei"
        };

        _userRepoMock
            .Setup(r => r.AnyAsync(
                It.IsAny<Expression<Func<User, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _mapperMock
            .Setup(m => m.Map<User>(command))
            .Returns(user);

        _mapperMock
            .Setup(m => m.Map<Address>(addresses[0]))
            .Returns(mappedAddress);

        _hasherMock
            .Setup(h => h.Hash(command.Password))
            .Returns("hashed");

        await _handler.Handle(command, CancellationToken.None);

        _addressRepoMock.Verify(
            r => r.AddRangeAsync(
                It.Is<IEnumerable<Address>>(items =>
                    items.Count() == 1 &&
                    items.Single().UserId == user.UserId &&
                    items.Single().Plaque == "12"),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _mapperMock.Verify(
            m => m.Map<Address>(addresses[1]),
            Times.Never);
    }

    [Fact]
    public async Task Handle_SuccessfulCreation_CommitsTransaction()
    {
        var command = CreateCommand();
        var user = CreateUser();

        _userRepoMock
            .Setup(r => r.AnyAsync(
                It.IsAny<Expression<Func<User, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _mapperMock
            .Setup(m => m.Map<User>(command))
            .Returns(user);

        _hasherMock
            .Setup(h => h.Hash(command.Password))
            .Returns("hashed");

        await _handler.Handle(command, CancellationToken.None);

        _uowMock.Verify(
            u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()),
            Times.Once);

        _uowMock.Verify(
            u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_PersistenceFailure_RollsBackTransaction()
    {
        var command = CreateCommand();
        var user = CreateUser();

        _userRepoMock
            .Setup(r => r.AnyAsync(
                It.IsAny<Expression<Func<User, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _mapperMock
            .Setup(m => m.Map<User>(command))
            .Returns(user);

        _hasherMock
            .Setup(h => h.Hash(command.Password))
            .Returns("hashed");

        _uowMock
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("persistence failed"));

        var act = () => _handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();

        _uowMock.Verify(
            u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>()),
            Times.Once);

        _uowMock.Verify(
            u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_SuccessfulCreation_InvalidatesAllUsersCache()
    {
        var command = CreateCommand();
        var user = CreateUser();

        _userRepoMock
            .Setup(r => r.AnyAsync(
                It.IsAny<Expression<Func<User, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _mapperMock
            .Setup(m => m.Map<User>(command))
            .Returns(user);

        _hasherMock
            .Setup(h => h.Hash(command.Password))
            .Returns("hashed");

        await _handler.Handle(command, CancellationToken.None);

        _cacheMock.Verify(
            c => c.RemoveAsync(
                "AllUsersFull",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_SuccessfulCreation_ReturnsCreatedUserId()
    {
        var command = CreateCommand();
        var user = CreateUser(42);

        _userRepoMock
            .Setup(r => r.AnyAsync(
                It.IsAny<Expression<Func<User, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _mapperMock
            .Setup(m => m.Map<User>(command))
            .Returns(user);

        _hasherMock
            .Setup(h => h.Hash(command.Password))
            .Returns("hashed");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Should().Be(42);
    }
}
