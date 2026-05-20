namespace SiemensS7Demo.Domain.Users;

public sealed record AuthResult(bool Success, User? User, string? ErrorMessage)
{
    public static AuthResult Ok(User user) => new(true, user, null);
    public static AuthResult Fail(string error) => new(false, null, error);
}
