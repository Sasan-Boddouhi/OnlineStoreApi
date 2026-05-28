using Application.Entities;
using Application.Helper;
using AutoMapper;
using BusinessLogic.DTOs.User;
using BusinessLogic.Common.Mapping;

namespace BusinessLogic.Profiles;

public class UserProfile : Profile
{
    public UserProfile()
    {
        // =========================
        // Entity -> DTO
        // =========================

        CreateMap<User, UserDto>()
            .ForMember(d => d.FullName,
                opt => opt.MapFrom(s => $"{s.FirstName} {s.LastName}"))

            .ForMember(d => d.EmployeeTypeId,
                opt => opt.MapFrom(s => s.Employee != null ? s.Employee.EmployeeTypeId : (int?)null))

            .ForMember(d => d.EmployeeTypeName,
                opt => opt.MapFrom(s => s.Employee != null ? s.Employee.EmployeeType.TypeName : null))

            .ForMember(d => d.EmployeeTypeDisplayName,
                opt => opt.MapFrom(s => s.Employee != null ? s.Employee.EmployeeType.DisplayName : null))

            .ForMember(d => d.DateOfBirthPersian,
                opt => opt.MapFrom(s => s.DateOfBirth.HasValue ? PersianDateHelper.ToPersian(s.DateOfBirth.Value) : null));

        // =========================
        // Create DTO -> Entity
        // =========================

        CreateMap<CreateUserDto, User>()
            .ConfigureDbDestination()

            .ForMember(d => d.UserId, opt => opt.Ignore())

            // security/internal
            .ForMember(d => d.PasswordHash, opt => opt.Ignore())
            .ForMember(d => d.SecurityStamp, opt => opt.Ignore())
            .ForMember(d => d.FailedLoginAttempts, opt => opt.Ignore())
            .ForMember(d => d.LockoutEnd, opt => opt.Ignore())

            // computed
            .ForMember(d => d.FullName, opt => opt.Ignore())

            // defaults
            .ForMember(d => d.IsActive, opt => opt.MapFrom(_ => true))
            .ForMember(d => d.UserType, opt => opt.MapFrom(_ => UserType.Customer));

        // =========================
        // Update DTO -> Entity
        // =========================

        CreateMap<UpdateUserDto, User>()
            .ConfigureDbDestination()

            // immutable/security
            .ForMember(d => d.PasswordHash, opt => opt.Ignore())
            .ForMember(d => d.UserType, opt => opt.Ignore())
            .ForMember(d => d.SecurityStamp, opt => opt.Ignore())
            .ForMember(d => d.FailedLoginAttempts, opt => opt.Ignore())
            .ForMember(d => d.LockoutEnd, opt => opt.Ignore())
            .ForMember(d => d.IsActive, opt => opt.Ignore())

            // computed
            .ForMember(d => d.FullName, opt => opt.Ignore())

            // null ignore
            .ForAllMembers(opts =>
                opts.Condition((src, dest, srcMember) => srcMember != null));
    }
}
