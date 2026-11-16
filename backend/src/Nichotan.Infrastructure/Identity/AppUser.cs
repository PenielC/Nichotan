using Microsoft.AspNetCore.Identity;

namespace Nichotan.Infrastructure.Identity;

/// <summary>A sign-in account (candidate or staff), stored by ASP.NET Core Identity.</summary>
public class AppUser : IdentityUser<Guid>
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;

    /// <summary>Set by an administrator. A disabled account can't sign in and its tokens stop working at once.</summary>
    public bool IsDisabled { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LastSignInAtUtc { get; set; }
}
