using IndustrialPlatform.Web.Querying;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

public static class QueryingServiceCollectionExtensions
{
    public static IServiceCollection AddIndustrialQuerying(
        this IServiceCollection services,
        ODataQueryValidationSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(new ODataQueryDescriptorParser(settings));
        return services;
    }
}
