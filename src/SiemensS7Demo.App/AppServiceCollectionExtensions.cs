using Microsoft.Extensions.DependencyInjection;
using SiemensS7Demo.Domain;

namespace SiemensS7Demo.App;

public static class AppServiceCollectionExtensions
{
    public static IServiceCollection AddSiemensS7DemoApp(this IServiceCollection services)
    {
        services.AddSingleton<IRbacContext, AdminRbacContext>();
        services.AddSingleton(sp =>
        {
            // Default 3-device InMemory config; the WPF app can override before resolve.
            var devs = new[]
            {
                new DeviceProvisioning("TH-01", "A1", DeviceType.Standard, "127.0.0.1", 102, "Mock", 0, 1,
                    "Pv", "Sv", "DB100.DBD10", "DB100.DBD14", true),
                new DeviceProvisioning("TH-02", "A2", DeviceType.Standard, "127.0.0.1", 102, "Mock", 0, 1,
                    "Pv", "Sv", "DB100.DBD10", "DB100.DBD14", true),
                new DeviceProvisioning("TH-03", "A3", DeviceType.Standard, "127.0.0.1", 102, "Mock", 0, 1,
                    "Pv", "Sv", "DB100.DBD10", "DB100.DBD14", true),
            };
            return new ProjectConfig(devs);
        });
        services.AddSingleton<IDeviceSessionManager, DeviceSessionManager>();
        return services;
    }
}
