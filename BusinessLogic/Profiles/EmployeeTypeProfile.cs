using Application.Entities;
using AutoMapper;
using BusinessLogic.DTOs.Employee;
using BusinessLogic.DTOs.EmployeeType;


namespace BusinessLogic.Profiles
{
    public class EmployeeTypeProfile : Profile
    {
        public EmployeeTypeProfile()
        {
            CreateMap<CreateEmployeeTypeDto, EmployeeType>();
            CreateMap<UpdateEmployeeTypeDto, EmployeeType>()
                .ForAllMembers(opts => opts.Condition((src, dest, srcMember) => srcMember != null));
            CreateMap<EmployeeType, EmployeeTypeDto>();
        }
    }

}
