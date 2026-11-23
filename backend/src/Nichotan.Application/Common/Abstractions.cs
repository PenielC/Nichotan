using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Nichotan.Domain.Admissions;
using Nichotan.Domain.Messaging;
using Nichotan.Domain.Reminders;
using Nichotan.Domain.Tasks;
using Nichotan.Domain.Visas;
using Nichotan.Domain.Applications;
using Nichotan.Domain.Audit;
using Nichotan.Domain.Candidates;
using Nichotan.Domain.Documents;
using Nichotan.Domain.Enquiries;
using Nichotan.Domain.Identity;

namespace Nichotan.Application.Common;

/// <summary>The database as the application sees it. Saving is explicit, so a change and its audit entry commit together.</summary>
public interface IAppDbContext
{
    DbSet<CandidateProfile> CandidateProfiles { get; }
    DbSet<ApplicationForm> ApplicationForms { get; }
    DbSet<ApplicationFormVersion> ApplicationFormVersions { get; }
    DbSet<DocumentType> DocumentTypes { get; }
    DbSet<DocumentRequirement> DocumentRequirements { get; }
    DbSet<ApplicationChecklistItem> ChecklistItems { get; }
    DbSet<CandidateDocument> CandidateDocuments { get; }
    DbSet<DocumentVersion> DocumentVersions { get; }
    DbSet<DocumentReview> DocumentReviews { get; }
    DbSet<AdmissionOffer> AdmissionOffers { get; }
    DbSet<AdmissionAcceptance> AdmissionAcceptances { get; }
    DbSet<Enquiry> Enquiries { get; }
    DbSet<SchoolSuggestion> SchoolSuggestions { get; }
    DbSet<Institution> Institutions { get; }
    DbSet<Programme> Programmes { get; }
    DbSet<AdmissionApplication> AdmissionApplications { get; }
    DbSet<AdmissionStatusEvent> AdmissionStatusEvents { get; }
    DbSet<VisaCase> VisaCases { get; }
    DbSet<VisaStatusEvent> VisaStatusEvents { get; }
    DbSet<PreDepartureTemplateItem> PreDepartureTemplate { get; }
    DbSet<PreDepartureCaveat> PreDepartureCaveats { get; }
    DbSet<PreDepartureList> PreDepartureLists { get; }
    DbSet<PreDepartureItem> PreDepartureItems { get; }
    DbSet<SentReminder> SentReminders { get; }
    DbSet<MessageLog> MessageLogs { get; }
    DbSet<StaffTask> StaffTasks { get; }
    DbSet<AuditEvent> AuditEvents { get; }
    DbSet<AuthToken> AuthTokens { get; }
    DbSet<RefreshToken> RefreshTokens { get; }

    /// <summary>For transactions around steps that also go through Identity (which saves on its own).</summary>
    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IClock
{
    DateTime UtcNow { get; }
}

/// <summary>Who is making the current request, as established by the API's authentication.</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
    string? Email { get; }
    IReadOnlyCollection<string> Roles { get; }

    /// <summary>Whether this sign-in passed a second factor.</summary>
    bool MfaSatisfied { get; }

    string? IpAddress { get; }
    bool IsInRole(string role);
}

/// <summary>Adds audit entries to the current unit of work; they are written by the next SaveChanges.</summary>
public interface IAuditLog
{
    void Add(
        AuditAction action,
        AuditOutcome outcome = AuditOutcome.Success,
        string? entityType = null,
        string? entityId = null,
        object? details = null,
        Guid? actorUserId = null,
        string? actorEmail = null);
}

/// <summary>A user account (identity data only; candidate details live on CandidateProfile).</summary>
public record UserAccount(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    bool EmailConfirmed,
    bool TwoFactorEnabled,
    bool IsDisabled,
    bool HasPassword,
    IReadOnlyList<string> Roles,
    DateTime CreatedAtUtc,
    DateTime? LastSignInAtUtc)
{
    public bool IsStaff => Roles.Any(Domain.Identity.Roles.IsStaffRole);
    public string FullName => $"{FirstName} {LastName}";
}

public enum PasswordCheck
{
    Success,
    Invalid,
    LockedOut,
    Disabled,
}

/// <summary>Account operations, backed by ASP.NET Core Identity in Infrastructure.</summary>
public interface IIdentityService
{
    Task<UserAccount?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<UserAccount?> FindByIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserAccount>> ListAsync(bool staffOnly, CancellationToken cancellationToken = default);

    /// <summary>Creates an account. Throws ConflictException if the email is taken, DomainException if the password is weak.</summary>
    Task<Guid> CreateAsync(string email, string? password, string firstName, string lastName, IReadOnlyCollection<string> roles, bool emailConfirmed, CancellationToken cancellationToken = default);

    /// <summary>Checks a password, counting failures towards lockout.</summary>
    Task<PasswordCheck> CheckPasswordAsync(Guid userId, string password, CancellationToken cancellationToken = default);

    /// <summary>Sets a new password (validated against the password policy). Throws DomainException if it's too weak.</summary>
    Task SetPasswordAsync(Guid userId, string newPassword, CancellationToken cancellationToken = default);

    Task ConfirmEmailAsync(Guid userId, CancellationToken cancellationToken = default);
    Task SetRolesAsync(Guid userId, IReadOnlyCollection<string> roles, CancellationToken cancellationToken = default);
    Task SetDisabledAsync(Guid userId, bool disabled, CancellationToken cancellationToken = default);
    Task SetNameAsync(Guid userId, string firstName, string lastName, CancellationToken cancellationToken = default);
    Task RecordSignInAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>The authenticator-app key (created if missing) and the otpauth:// URI to show as a QR code.</summary>
    Task<(string SharedKey, string AuthenticatorUri)> GetAuthenticatorSetupAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Checks a 6-digit authenticator code, counting failures towards lockout.</summary>
    Task<bool> VerifyAuthenticatorCodeAsync(Guid userId, string code, CancellationToken cancellationToken = default);

    Task<bool> RedeemRecoveryCodeAsync(Guid userId, string code, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GenerateRecoveryCodesAsync(Guid userId, CancellationToken cancellationToken = default);
    Task SetTwoFactorEnabledAsync(Guid userId, bool enabled, CancellationToken cancellationToken = default);
}

public record AccessToken(string Token, DateTime ExpiresAtUtc);

public interface ITokenService
{
    AccessToken CreateAccessToken(UserAccount user, bool mfaSatisfied);

    /// <summary>A short-lived token that only allows completing the second step of a sign-in.</summary>
    string CreateMfaChallengeToken(Guid userId);

    Guid? ReadMfaChallengeToken(string token);
}

/// <summary>
/// Which of Nichotan's addresses an email is sent from. The actual addresses are configuration, not code.
/// If the Info address is not configured, Info messages go out from the Automated one, so nothing breaks.
/// </summary>
public enum EmailSenderKind
{
    /// <summary>System mail nobody is expected to answer: account links, notices to staff. Replies are directed to the Info mailbox.</summary>
    Automated = 0,

    /// <summary>Mail from the team to a student, where a reply is welcome (e.g. info@).</summary>
    Info = 1,
}

public record EmailMessage(string To, string Subject, string TextBody, string HtmlBody, EmailSenderKind From = EmailSenderKind.Automated);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

public interface IAppUrls
{
    /// <summary>The web app's address, for links in emails (no trailing slash).</summary>
    string WebBaseUrl { get; }
}

/// <summary>Private document storage. Objects are never public; access goes through short-lived signed URLs.</summary>
public interface IObjectStorage
{
    Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default);
    Task<Stream> GetAsync(string key, CancellationToken cancellationToken = default);
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>A URL that allows downloading one object for a short time.</summary>
    string GetDownloadUrl(string key, TimeSpan lifetime, string? downloadFileName = null);

    /// <summary>Health check: the bucket exists and is reachable.</summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
}

/// <summary>Where enquiries from the website are announced (bound from the "Enquiries" configuration section).</summary>
public class EnquiryOptions
{
    /// <summary>The mailbox told about each new enquiry. Leave empty to turn the notification off.</summary>
    public string? NotifyEmail { get; set; }
}

/// <summary>Who is told when a student sends an application for review (bound from the "Admissions" configuration section).</summary>
public class AdmissionNotificationOptions
{
    /// <summary>The mailbox told about each application sent for review. Leave empty to turn the notification off.</summary>
    public string? NotifyEmail { get; set; }
}

/// <summary>Security settings that change behaviour (bound from the "Security" configuration section).</summary>
public class SecurityOptions
{
    /// <summary>Staff must sign in with an authenticator app before using staff features. Leave on in production.</summary>
    public bool RequireMfaForStaff { get; set; } = true;
}
