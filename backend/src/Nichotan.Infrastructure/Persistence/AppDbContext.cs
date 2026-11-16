using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Nichotan.Application.Common;
using Nichotan.Domain.Admissions;
using Nichotan.Domain.Messaging;
using Nichotan.Domain.Reminders;
using Nichotan.Domain.Tasks;
using Nichotan.Domain.Visas;
using Nichotan.Domain.Applications;
using Nichotan.Domain.Audit;
using Nichotan.Domain.Candidates;
using Nichotan.Domain.Common;
using Nichotan.Domain.Documents;
using Nichotan.Domain.Enquiries;
using Nichotan.Domain.Identity;
using Nichotan.Infrastructure.Identity;

namespace Nichotan.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options, IClock clock)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options), IAppDbContext
{
    public DbSet<CandidateProfile> CandidateProfiles => Set<CandidateProfile>();
    public DbSet<ApplicationForm> ApplicationForms => Set<ApplicationForm>();
    public DbSet<ApplicationFormVersion> ApplicationFormVersions => Set<ApplicationFormVersion>();
    public DbSet<DocumentType> DocumentTypes => Set<DocumentType>();
    public DbSet<DocumentRequirement> DocumentRequirements => Set<DocumentRequirement>();
    public DbSet<ApplicationChecklistItem> ChecklistItems => Set<ApplicationChecklistItem>();
    public DbSet<CandidateDocument> CandidateDocuments => Set<CandidateDocument>();
    public DbSet<DocumentVersion> DocumentVersions => Set<DocumentVersion>();
    public DbSet<DocumentReview> DocumentReviews => Set<DocumentReview>();
    public DbSet<AdmissionOffer> AdmissionOffers => Set<AdmissionOffer>();
    public DbSet<AdmissionAcceptance> AdmissionAcceptances => Set<AdmissionAcceptance>();
    public DbSet<Enquiry> Enquiries => Set<Enquiry>();
    public DbSet<SchoolSuggestion> SchoolSuggestions => Set<SchoolSuggestion>();
    public DbSet<Institution> Institutions => Set<Institution>();
    public DbSet<Programme> Programmes => Set<Programme>();
    public DbSet<AdmissionApplication> AdmissionApplications => Set<AdmissionApplication>();
    public DbSet<AdmissionStatusEvent> AdmissionStatusEvents => Set<AdmissionStatusEvent>();
    public DbSet<VisaCase> VisaCases => Set<VisaCase>();
    public DbSet<VisaStatusEvent> VisaStatusEvents => Set<VisaStatusEvent>();
    public DbSet<PreDepartureTemplateItem> PreDepartureTemplate => Set<PreDepartureTemplateItem>();
    public DbSet<PreDepartureCaveat> PreDepartureCaveats => Set<PreDepartureCaveat>();
    public DbSet<PreDepartureList> PreDepartureLists => Set<PreDepartureList>();
    public DbSet<PreDepartureItem> PreDepartureItems => Set<PreDepartureItem>();
    public DbSet<SentReminder> SentReminders => Set<SentReminder>();
    public DbSet<MessageLog> MessageLogs => Set<MessageLog>();
    public DbSet<StaffTask> StaffTasks => Set<StaffTask>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<AuthToken> AuthTokens => Set<AuthToken>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    DatabaseFacade IAppDbContext.Database => Database;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Plain table names for Identity's tables.
        builder.Entity<AppUser>(b =>
        {
            b.ToTable("Users");
            b.Property(u => u.FirstName).HasMaxLength(100).IsRequired();
            b.Property(u => u.LastName).HasMaxLength(100).IsRequired();
        });
        builder.Entity<IdentityRole<Guid>>().ToTable("Roles");
        builder.Entity<IdentityUserRole<Guid>>().ToTable("UserRoles");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("UserClaims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("UserLogins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("UserTokens");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("RoleClaims");

        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        foreach (var entry in ChangeTracker.Entries<Entity>())
        {
            if (entry.State == EntityState.Added && entry.Entity.CreatedAtUtc == default) entry.Entity.CreatedAtUtc = now;
            else if (entry.State == EntityState.Modified) entry.Entity.UpdatedAtUtc = now;
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
