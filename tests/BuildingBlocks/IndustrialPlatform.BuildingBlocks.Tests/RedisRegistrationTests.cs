using IndustrialPlatform.Infrastructure.Caching;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Xunit;

namespace IndustrialPlatform.BuildingBlocks.Tests;

public sealed class RedisRegistrationTests
{
    [Fact]
    public void AddRedis_RegistersCacheServiceDistributedLockAndMultiplexer()
    {
        var services = new ServiceCollection();
        services.AddRedis(options => options.ConnectionString = "localhost:6379");

        Assert.Contains(services, service => service.ServiceType == typeof(ICacheService));
        Assert.Contains(services, service => service.ServiceType == typeof(IDistributedLock));
        Assert.Contains(services, service => service.ServiceType == typeof(IConnectionMultiplexer));
    }
}
