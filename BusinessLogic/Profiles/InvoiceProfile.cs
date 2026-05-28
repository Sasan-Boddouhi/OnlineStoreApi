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

        CreateMap<CreateInvoiceDto, Invoice>()
            .ConfigureDbDestination()
            .ForMember(d => d.InvoiceId, opt => opt.Ignore())
            .ForMember(d => d.Status, opt => opt.Ignore()) 
            .ForMember(d => d.PaidDate, opt => opt.Ignore());

        CreateMap<UpdateInvoiceDto, Invoice>()
            .ConfigureDbDestination()
            .ForMember(d => d.InvoiceId, opt => opt.Ignore())
            .ForMember(d => d.Status, opt => opt.Ignore())
            .ForMember(d => d.PaidDate, opt => opt.Ignore())
            .ForAllMembers(opts =>
                opts.Condition((src, dest, srcMember) => srcMember != null));
    }
}
