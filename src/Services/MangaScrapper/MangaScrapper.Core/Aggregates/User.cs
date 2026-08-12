using MangaScrapper.Core.ValueObjects;
using NovaStack.SharedKernel.Common;

namespace MangaScrapper.Core.Aggregates;

public class User : Entity<UserId>
{
    public static class UserRoles
    {
        public const string SuperUser = "SuperUser";
        public const string Admin = "Admin";
        public const string User = "User";
    }

    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
    public bool IsActive { get; set; } = true;
    public string? FirebaseUid { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastActiveAt { get; set; }
    
    
    private User(UserId id, string username, string passwordHash, string email, List<string> roles, bool isActive, string? firebaseUid, DateTime? createdAt, DateTime? lastActiveAt)
        : base(id)
    {
        Username = username;
        PasswordHash = passwordHash;
        Email = email;
        Roles = roles;
        IsActive = isActive;
        FirebaseUid = firebaseUid;
        CreatedAt = createdAt ?? DateTime.UtcNow;
        LastActiveAt = lastActiveAt;
    }
    
    
    public static User Create(UserId id, string username, string passwordHash, string email, List<string> roles, string? firebaseUid = null, DateTime? lastActiveAt = null)
    {
        return new User(id, username, passwordHash, email, roles, true, firebaseUid, DateTime.UtcNow, lastActiveAt);
    }

    public static User Reconstitute(UserId id, string username, string passwordHash, string email, List<string> roles, bool isActive, string? firebaseUid, DateTime? createdAt, DateTime? lastActiveAt)
    {
        return new User(id, username, passwordHash, email, roles, isActive, firebaseUid, createdAt, lastActiveAt);
    }
}