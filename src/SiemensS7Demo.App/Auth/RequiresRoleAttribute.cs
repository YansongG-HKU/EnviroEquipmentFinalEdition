using System;
using SiemensS7Demo.Domain.Users;

namespace SiemensS7Demo.App.Auth;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class RequiresRoleAttribute : Attribute
{
    public RequiresRoleAttribute(Role minimum) { Minimum = minimum; }
    public Role Minimum { get; }
}
