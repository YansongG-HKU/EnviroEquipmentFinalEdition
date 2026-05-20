namespace SiemensS7Demo.Domain.Users;

public sealed record User(string Id, string Name, Role Role, string Code, string PasswordHash);
