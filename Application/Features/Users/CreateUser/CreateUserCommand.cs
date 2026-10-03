using MediatR;

namespace Application.Features.Users.CreateUser;

public sealed record CreateUserCommand : IRequest<int>;
