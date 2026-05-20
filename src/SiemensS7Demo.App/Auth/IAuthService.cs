using System.Threading;
using System.Threading.Tasks;
using SiemensS7Demo.Domain.Users;

namespace SiemensS7Demo.App.Auth;

public interface IAuthService
{
    User? Current { get; }
    Shift? CurrentShift { get; }
    Task<AuthResult> SignInAsync(string code, string password, Shift shift, CancellationToken ct);
    void SignOut();
}
