using Application.Entities;
using AutoMapper;
using BusinessLogic.DTOs.Invoice;
using BusinessLogic.Common.Mapping;

namespace BusinessLogic.Profiles;

public class InvoiceProfile : Profile
{
    public InvoiceProfile()
    {
        CreateMap<Invoice, InvoiceDto>()
            .ForMember(dest => dest.Status, opt => opt.MapFrom(src => src.Status.ToString()));
    }
}
