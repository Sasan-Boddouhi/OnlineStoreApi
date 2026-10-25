using Application.Entities;
using AutoMapper;
using BusinessLogic.DTOs.Employee;
using BusinessLogic.Common.Mapping; // اضافه کردن فضای نام افزونه جدید

namespace BusinessLogic.Profiles;

public class EmployeeProfile : Profile
{
    public EmployeeProfile()
    {
        CreateMap<CreateEmployeeDto, Employee>()
            .ConfigureDbDestination()
            .ForMember(d => d.EmployeeId, opt => opt.Ignore())
            .ForMember(d => d.TerminationDate, opt => opt.Ignore());

        CreateMap<UpdateEmployeeDto, Employee>()
            .ConfigureDbDestination()
            .ForMember(d => d.UserId, opt => opt.Ignore())
            .ForMember(d => d.TerminationDate, opt => opt.Ignore())
            .ForAllMembers(opts =>
                opts.Condition((src, dest, srcMember) => srcMember != null));

        CreateMap<Employee, EmployeeDto>()
            .ForMember(d => d.UserFullName, opt => opt.MapFrom(s => s.User != null ? s.User.FullName : null))
            .ForMember(d => d.PhoneNumber, opt => opt.MapFrom(s => s.User != null ? s.User.PhoneNumber : null))
            .ForMember(d => d.EmployeeTypeName, opt => opt.MapFrom(s => s.EmployeeType != null ? s.EmployeeType.TypeName : null))
            .ForMember(d => d.IsActive, opt => opt.MapFrom(s => s.User != null ? s.User.IsActive : false));
    }
}
