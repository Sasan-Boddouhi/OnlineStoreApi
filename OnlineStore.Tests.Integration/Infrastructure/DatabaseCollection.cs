using OnlineStore.Tests.Integration.Infrastructure;
using Xunit;

namespace OnlineStore.Tests.Integration.Fixtures;

[CollectionDefinition("DatabaseCollection")]
public class DatabaseCollection : ICollectionFixture<IntegrationTestFactory<Program>>
{
    // فقط برای تعریف مجموعه
}