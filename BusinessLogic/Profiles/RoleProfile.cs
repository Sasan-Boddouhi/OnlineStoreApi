using Application.Entities;
using AutoMapper;
using BusinessLogic.DTOs.Role;
using BusinessLogic.DTOs.Permission;
using BusinessLogic.Common.Mapping;

namespace BusinessLogic.Profiles;

public class RoleProfile : Profile
{
    public RoleProfile()
    {
        CreateMap<CreateRoleDto, Role>()
            .ConfigureDbDestination()
            .ForMember(d => d.RoleId, opt => opt.Ignore())
            .ForMember(d => d.IsSystem, opt => opt.MapFrom(src => false))
            .ForMember(d => d.RolePermissions, opt => opt.Ignore());

        CreateMap<UpdateRoleDto, Role>()
            .ConfigureDbDestination()
            .ForMember(d => d.RoleId, opt => opt.Ignore())
            .ForMember(d => d.IsSystem, opt => opt.Ignore())
            .ForMember(d => d.RolePermissions, opt => opt.Ignore());

        CreateMap<Role, RoleDto>()
            .ForMember(d => d.Permissions, opt => opt.Ignore()); // Loaded separately

        CreateMap<Permission, PermissionDto>();
    }
}