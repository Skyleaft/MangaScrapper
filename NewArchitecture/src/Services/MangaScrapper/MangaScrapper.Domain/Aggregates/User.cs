using MangaScrapper.Domain.ValueObjects;
using NovaStack.SharedKernel.Common;

namespace MangaScrapper.Domain.Aggregates;

public class User : Entity<UserId>
{
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
    public bool IsActive { get; set; } = true;
    public string? FirebaseUid { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    
    private User(UserId id, string username, string passwordHash, string email, List<string> roles, bool isActive, string? firebaseUid, DateTime? createdAt)
        : base(id)
    {
        Username = username;
        PasswordHash = passwordHash;
        Email = email;
        Roles = roles;
        IsActive = isActive;
        FirebaseUid = firebaseUid;
        CreatedAt = createdAt ?? DateTime.UtcNow;
    }
    
    
    public static User Create(UserId id, string username, string passwordHash, string email, List<string> roles, string? firebaseUid = null)
    {
        return new User(id, username, passwordHash, email, roles, true, firebaseUid, DateTime.UtcNow);
    }

    public static User Reconstitute(UserId id, string username, string passwordHash, string email, List<string> roles, bool isActive, string? firebaseUid, DateTime? createdAt)
    {
        return new User(id, username, passwordHash, email, roles, isActive, firebaseUid, createdAt);
    }
}