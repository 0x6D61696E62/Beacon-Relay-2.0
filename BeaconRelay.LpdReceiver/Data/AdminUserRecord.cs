namespace BeaconRelay.LpdReceiver.Data;

public sealed class AdminUserRecord
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Role { get; set; } = AdminRoles.ReadOnly;
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

public static class AdminRoles
{
    public const string Admin = "Admin";
    public const string Settings = "Settings";
    public const string ReadOnly = "ReadOnly";
}
