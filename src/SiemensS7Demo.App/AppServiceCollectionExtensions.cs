using Microsoft.Extensions.DependencyInjection;

namespace SiemensS7Demo.App;

public static class AppServiceCollectionExtensions
{
    public static IServiceCollection AddSiemensS7DemoApp(this IServiceCollection services)
    {
        // Real registrations land in Task 3. M1.1 only needs the method to exist.
        return services;
    }
}
