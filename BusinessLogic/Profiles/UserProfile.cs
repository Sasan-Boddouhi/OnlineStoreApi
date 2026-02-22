using Application.Entities;
using AutoMapper;
using BusinessLogic.DTOs.User;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace BusinessLogic.Profiles
{
    public class UserProfile : Profile
    {
        public UserProfile()
        {
            CreateMap<User, UserDto>()
                .ForMember(dest => dest.UserTypeName,
                    opt => opt.MapFrom(src => src.UserType.ToString()))

                .ForMember(dest => dest.FullName,
                    opt => opt.MapFrom(src => (src.FirstName ?? "") + " " + (src.LastName ?? "")))

                .ForMember(dest => dest.RoleName,
                    opt => opt.MapFrom(src =>
                        src.Employee != null &&
                        src.Employee.EmployeeType != null
                            ? src.Employee.EmployeeType.TypeName
                            : "بدون نقش"))

                .ForMember(dest => dest.Addresses,
                    opt => opt.MapFrom(src => src.Addresses));

            CreateMap<CreateUserDto, User>()
                .ForMember(dest => dest.PasswordHash, opt => opt.Ignore())
                .ForMember(dest => dest.IsActive, opt => opt.MapFrom(src => true))
                .ForMember(dest => dest.UserType,
                    opt => opt.MapFrom(src => (UserType)src.UserType))
                .ForMember(dest => dest.Addresses, opt => opt.Ignore());

            CreateMap<UpdateUserDto, User>();
        }
    }

}
