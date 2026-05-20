using System.Reflection;
using SiemensS7Demo.Domain.Users;

namespace SiemensS7Demo.App.Auth;

public static class RbacGuard
{
    public static bool IsAllowed(User? user, MethodInfo method)
    {
        var attr = method.GetCustomAttribute<RequiresRoleAttribute>(inherit: true);
        if (attr is null) return true;
        if (user is null) return false;
        return user.Role >= attr.Minimum;
    }

    public static bool IsAllowed(User? user, Role? minimum)
    {
        if (minimum is null) return true;
        if (user is null) return false;
        return user.Role >= minimum.Value;
    }

    public static Role? MinimumFor(MethodInfo method)
        => method.GetCustomAttribute<RequiresRoleAttribute>(inherit: true)?.Minimum;
}
