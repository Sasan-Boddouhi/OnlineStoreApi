using Application.Entities;
using AutoMapper;
using BusinessLogic.DTOs.Payout;

namespace BusinessLogic.Profiles;

public class PayoutProfile : Profile
{
    public PayoutProfile()
    {
        CreateMap<SellerPayout, SellerPayoutDto>()
            .ForMember(d => d.Status, opt => opt.MapFrom(s => s.Status.ToString()));
    }
}
