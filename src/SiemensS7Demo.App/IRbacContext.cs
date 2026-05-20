namespace SiemensS7Demo.App;

public interface IRbacContext
{
    Role Current { get; }
    bool IsAtLeast(Role minimum);
}
