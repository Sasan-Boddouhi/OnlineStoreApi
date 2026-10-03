using MediatR;

namespace Application.Features.Users.CreateUser;

public sealed record CreateUserCommand(
    string FirstName,
    string LastName,
    string PhoneNumber,
    string Password,
    string? Email,
    string? DateOfBirth,
    IReadOnlyList<CreateUserAddressCommand>? Addresses) : IRequest<int>;

public sealed record CreateUserAddressCommand(
    int CityId,
    string Plaque,
    string Unit,
    string PostalCode,
    string RecipientFirstName,
    string RecipientLastName,
    string? ExtraDescription,
    bool IsDefault);