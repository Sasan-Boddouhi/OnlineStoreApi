using Application.Entities;
using AutoMapper;
using BusinessLogic.DTOs.EmployeeType;
using BusinessLogic.Common.Mapping;

namespace BusinessLogic.Profiles;

public class EmployeeTypeProfile : Profile
{
    public EmployeeTypeProfile()
    {
        CreateMap<CreateEmployeeTypeDto, EmployeeType>()
            .ConfigureDbDestination()
            .ForMember(d => d.EmployeeTypeId, opt => opt.Ignore())
            .ForMember(d => d.IsSystem, opt => opt.Ignore())
            .ForMember(d => d.IsActive, opt => opt.Ignore());

        CreateMap<UpdateEmployeeTypeDto, EmployeeType>()
            .ConfigureDbDestination()
            .ForMember(d => d.EmployeeTypeId, opt => opt.Ignore())
            .ForMember(d => d.IsSystem, opt => opt.Ignore())
            .ForAllMembers(opts =>
                opts.Condition((src, dest, srcMember) => srcMember != null));

        CreateMap<EmployeeType, EmployeeTypeDto>();
    }
}
