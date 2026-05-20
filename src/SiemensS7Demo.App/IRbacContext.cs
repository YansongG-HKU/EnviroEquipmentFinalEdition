using SiemensS7Demo.Domain.Users;

namespace SiemensS7Demo.App;

public interface IRbacContext
{
    Role Current { get; }
    bool IsAtLeast(Role minimum);
}
