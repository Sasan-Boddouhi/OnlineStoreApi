using AutoMapper;
using BusinessLogic.Profiles;
using Xunit;

namespace OnlineStore.Tests.Unit.Mappings;

public class AutoMapperConfigurationTests
{
    [Fact]
    public void AllProfiles_ShouldBeValid()
    {
        var config = new MapperConfiguration(cfg =>
        {
            cfg.AddMaps(typeof(ProductProfile).Assembly);
        });

        config.AssertConfigurationIsValid();
    }
}
