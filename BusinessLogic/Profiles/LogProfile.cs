using Application.Entities;
using AutoMapper;
using BusinessLogic.DTOs.Log;
using BusinessLogic.Common.Mapping;

namespace BusinessLogic.Profiles;

public class LogProfile : Profile
{
    public LogProfile()
    {
        CreateMap<Logs, LogEntryDto>();

        CreateMap<LogEntryDto, Logs>()
            .ConfigureDbDestination()
            .ForMember(d => d.MessageTemplate, opt => opt.Ignore())
            .ForMember(d => d.Properties, opt => opt.Ignore());
    }
}
