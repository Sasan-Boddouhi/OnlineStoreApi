using Application.Entities;
using AutoMapper;

namespace Application.Features.Users.CreateUser;

public sealed class CreateUserMappingProfile : Profile
{
    public CreateUserMappingProfile()
    {
        CreateMap<CreateUserCommand, User>()
            .ForMember(d => d.UserId, opt => opt.Ignore())
            .ForMember(d => d.PasswordHash, opt => opt.Ignore())
            .ForMember(d => d.SecurityStamp, opt => opt.Ignore())
            .ForMember(d => d.FailedLoginAttempts, opt => opt.Ignore())
            .ForMember(d => d.LockoutEnd, opt => opt.Ignore())
            .ForMember(d => d.FullName, opt => opt.Ignore())
            .ForMember(d => d.DateOfBirth, opt => opt.Ignore())
            .ForMember(d => d.Addresses, opt => opt.Ignore())
            .ForMember(d => d.Sessions, opt => opt.Ignore())
            .ForMember(d => d.Customer, opt => opt.Ignore())
            .ForMember(d => d.Employee, opt => opt.Ignore())
            .ForMember(d => d.CreatedOn, opt => opt.Ignore())
            .ForMember(d => d.CreatedById, opt => opt.Ignore())
            .ForMember(d => d.ModifiedOn, opt => opt.Ignore())
            .ForMember(d => d.ModifiedById, opt => opt.Ignore())
            .ForMember(d => d.IsActive, opt => opt.MapFrom(_ => true))
            .ForMember(d => d.UserType, opt => opt.MapFrom(_ => UserType.Customer));

        CreateMap<CreateUserAddressCommand, Address>()
            .ForMember(d => d.AddressId, opt => opt.Ignore())
            .ForMember(d => d.UserId, opt => opt.Ignore())
            .ForMember(d => d.City, opt => opt.Ignore())
            .ForMember(d => d.User, opt => opt.Ignore())
            .ForMember(d => d.Warehouses, opt => opt.Ignore())
            .ForMember(d => d.CreatedOn, opt => opt.Ignore())
            .ForMember(d => d.CreatedById, opt => opt.Ignore())
            .ForMember(d => d.ModifiedOn, opt => opt.Ignore())
            .ForMember(d => d.ModifiedById, opt => opt.Ignore());
    }
}