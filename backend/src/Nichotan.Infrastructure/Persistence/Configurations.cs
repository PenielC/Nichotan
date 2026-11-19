using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
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
using Nichotan.Infrastructure.Identity;

namespace Nichotan.Infrastructure.Persistence;

public class CandidateProfileConfiguration : IEntityTypeConfiguration<CandidateProfile>
{
    public void Configure(EntityTypeBuilder<CandidateProfile> b)
    {
        b.ToTable("CandidateProfiles");
        b.HasKey(p => p.Id);
        b.HasIndex(p => p.UserId).IsUnique();
        b.HasIndex(p => p.Email);
        b.Property(p => p.FirstName).HasMaxLength(100).IsRequired();
        b.Property(p => p.LastName).HasMaxLength(100).IsRequired();
        b.Property(p => p.Email).HasMaxLength(256).IsRequired();
        b.Property(p => p.Phone).HasMaxLength(30);
        b.Ignore(p => p.FullName);
        b.HasOne<AppUser>().WithOne().HasForeignKey<CandidateProfile>(p => p.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class ApplicationFormConfiguration : IEntityTypeConfiguration<ApplicationForm>
{
    public void Configure(EntityTypeBuilder<ApplicationForm> b)
    {
        b.ToTable("ApplicationForms");
        b.HasKey(f => f.Id);
        b.HasIndex(f => f.CandidateId).IsUnique();
        b.Property(f => f.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(f => f.DataJson).HasColumnType("jsonb");
        b.Property(f => f.ReopenReason).HasMaxLength(1000);
        b.Ignore(f => f.IsEditable);
        b.HasOne<CandidateProfile>().WithOne().HasForeignKey<ApplicationForm>(f => f.CandidateId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(f => f.Versions).WithOne().HasForeignKey(v => v.FormId).OnDelete(DeleteBehavior.Restrict);
        b.Navigation(f => f.Versions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class ApplicationFormVersionConfiguration : IEntityTypeConfiguration<ApplicationFormVersion>
{
    public void Configure(EntityTypeBuilder<ApplicationFormVersion> b)
    {
        b.ToTable("ApplicationFormVersions");
        b.HasKey(v => v.Id);
        // Two simultaneous submissions can't both create the same version.
        b.HasIndex(v => new { v.FormId, v.VersionNumber }).IsUnique();
        b.Property(v => v.DataJson).HasColumnType("jsonb");
        b.Property(v => v.ContentHash).HasMaxLength(64).IsRequired();
        b.OwnsOne(v => v.Signature, s =>
        {
            s.Property(x => x.Provider).HasMaxLength(40).IsRequired();
            s.Property(x => x.SignerName).HasMaxLength(200).IsRequired();
            s.Property(x => x.IpAddress).HasMaxLength(64);
            s.Property(x => x.ExternalReference).HasMaxLength(200);
            s.Property(x => x.DeclarationVersion).HasMaxLength(20).IsRequired();
            s.Property(x => x.DeclarationText).HasMaxLength(2000).IsRequired();
        });
        b.Navigation(v => v.Signature).IsRequired();
    }
}

public class InstitutionConfiguration : IEntityTypeConfiguration<Institution>
{
    public void Configure(EntityTypeBuilder<Institution> b)
    {
        b.ToTable("Institutions");
        b.HasKey(i => i.Id);
        b.Property(i => i.Name).HasMaxLength(200).IsRequired();
        b.Property(i => i.Country).HasConversion<string>().HasMaxLength(20);
        b.Property(i => i.Website).HasMaxLength(500);
        b.Property(i => i.Notes).HasMaxLength(2000);
        b.HasIndex(i => new { i.Country, i.Name }).IsUnique();
    }
}

public class ProgrammeConfiguration : IEntityTypeConfiguration<Programme>
{
    public void Configure(EntityTypeBuilder<Programme> b)
    {
        b.ToTable("Programmes");
        b.HasKey(p => p.Id);
        b.Property(p => p.Name).HasMaxLength(200).IsRequired();
        b.Property(p => p.Level).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(p => new { p.InstitutionId, p.Name, p.Level }).IsUnique();
        b.HasOne<Institution>().WithMany().HasForeignKey(p => p.InstitutionId).OnDelete(DeleteBehavior.Restrict);
        b.OwnsMany(p => p.Intakes, o =>
        {
            o.ToTable("ProgrammeIntakes");
            o.WithOwner().HasForeignKey("ProgrammeId");
            o.Property<int>("Id").ValueGeneratedOnAdd();
            o.HasKey("Id");
            o.Property(i => i.Term).HasConversion<string>().HasMaxLength(20);
            o.HasIndex("ProgrammeId", nameof(ProgrammeIntake.Term), nameof(ProgrammeIntake.Year)).IsUnique();
        });
        b.Navigation(p => p.Intakes).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class AdmissionApplicationConfiguration : IEntityTypeConfiguration<AdmissionApplication>
{
    public void Configure(EntityTypeBuilder<AdmissionApplication> b)
    {
        b.ToTable("AdmissionApplications");
        b.HasKey(a => a.Id);
        b.Property(a => a.Country).HasConversion<string>().HasMaxLength(20);
        b.Property(a => a.Level).HasConversion<string>().HasMaxLength(20);
        b.Property(a => a.IntakeTerm).HasConversion<string>().HasMaxLength(20);
        b.Property(a => a.Status).HasConversion<string>().HasMaxLength(30);
        b.Property(a => a.InstitutionReference).HasMaxLength(100);
        b.Property(a => a.PortalUrl).HasMaxLength(500);
        b.HasIndex(a => a.CandidateId);
        b.HasIndex(a => a.Status);
        b.HasOne<CandidateProfile>().WithMany().HasForeignKey(a => a.CandidateId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Institution>().WithMany().HasForeignKey(a => a.InstitutionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Programme>().WithMany().HasForeignKey(a => a.ProgrammeId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class AdmissionStatusEventConfiguration : IEntityTypeConfiguration<AdmissionStatusEvent>
{
    public void Configure(EntityTypeBuilder<AdmissionStatusEvent> b)
    {
        b.ToTable("AdmissionStatusEvents");
        b.HasKey(e => e.Id);
        b.Property(e => e.From).HasConversion<string>().HasMaxLength(30);
        b.Property(e => e.To).HasConversion<string>().HasMaxLength(30);
        b.Property(e => e.RecordedByEmail).HasMaxLength(256);
        b.Property(e => e.Note).HasMaxLength(2000);
        b.HasIndex(e => new { e.ApplicationId, e.RecordedAtUtc });
        b.HasIndex(e => new { e.To, e.OccurredOn });
        b.HasOne<AdmissionApplication>().WithMany().HasForeignKey(e => e.ApplicationId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class DocumentTypeConfiguration : IEntityTypeConfiguration<DocumentType>
{
    public void Configure(EntityTypeBuilder<DocumentType> b)
    {
        b.ToTable("DocumentTypes");
        b.HasKey(t => t.Id);
        b.Property(t => t.Name).HasMaxLength(150).IsRequired();
        b.Property(t => t.Description).HasMaxLength(1000);
        b.Property(t => t.Scope).HasConversion<string>().HasMaxLength(20);
        b.Property(t => t.Purpose).HasConversion<string>().HasMaxLength(30).HasDefaultValue(DocumentPurpose.None);
        b.Property(t => t.AllowsMultipleFiles).HasDefaultValue(false);
        b.HasIndex(t => t.Name).IsUnique();
    }
}

public class DocumentRequirementConfiguration : IEntityTypeConfiguration<DocumentRequirement>
{
    public void Configure(EntityTypeBuilder<DocumentRequirement> b)
    {
        b.ToTable("DocumentRequirements");
        b.HasKey(r => r.Id);
        b.Property(r => r.Stage).HasConversion<string>().HasMaxLength(20).HasDefaultValue(ChecklistStage.Admission);
        b.Property(r => r.Country).HasConversion<string>().HasMaxLength(20);
        b.Property(r => r.Level).HasConversion<string>().HasMaxLength(20);
        b.Ignore(r => r.Specificity);
        b.HasOne<DocumentType>().WithMany().HasForeignKey(r => r.DocumentTypeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Institution>().WithMany().HasForeignKey(r => r.InstitutionId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class ApplicationChecklistItemConfiguration : IEntityTypeConfiguration<ApplicationChecklistItem>
{
    public void Configure(EntityTypeBuilder<ApplicationChecklistItem> b)
    {
        b.ToTable("ApplicationChecklistItems");
        b.HasKey(i => i.Id);
        b.Property(i => i.Source).HasConversion<string>().HasMaxLength(20);
        b.Property(i => i.Stage).HasConversion<string>().HasMaxLength(20).HasDefaultValue(ChecklistStage.Admission);
        b.Property(i => i.Note).HasMaxLength(1000);
        b.HasIndex(i => new { i.ApplicationId, i.Stage, i.DocumentTypeId }).IsUnique();
        b.HasOne<AdmissionApplication>().WithMany().HasForeignKey(i => i.ApplicationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<DocumentType>().WithMany().HasForeignKey(i => i.DocumentTypeId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class CandidateDocumentConfiguration : IEntityTypeConfiguration<CandidateDocument>
{
    public void Configure(EntityTypeBuilder<CandidateDocument> b)
    {
        b.ToTable("CandidateDocuments");
        b.HasKey(d => d.Id);
        b.Property(d => d.Status).HasConversion<string>().HasMaxLength(30);
        // One slot per candidate and type (and application, for application-scoped types).
        b.HasIndex(d => new { d.CandidateId, d.DocumentTypeId, d.ApplicationId }).IsUnique().AreNullsDistinct(false);
        b.HasIndex(d => d.Status);
        b.HasOne<CandidateProfile>().WithMany().HasForeignKey(d => d.CandidateId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<DocumentType>().WithMany().HasForeignKey(d => d.DocumentTypeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AdmissionApplication>().WithMany().HasForeignKey(d => d.ApplicationId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(d => d.Versions).WithOne().HasForeignKey(v => v.DocumentId).OnDelete(DeleteBehavior.Restrict);
        b.Navigation(d => d.Versions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class DocumentVersionConfiguration : IEntityTypeConfiguration<DocumentVersion>
{
    public void Configure(EntityTypeBuilder<DocumentVersion> b)
    {
        b.ToTable("DocumentVersions");
        b.HasKey(v => v.Id);
        b.Property(v => v.StorageKey).HasMaxLength(300).IsRequired();
        b.Property(v => v.FileName).HasMaxLength(255).IsRequired();
        b.Property(v => v.ContentType).HasMaxLength(100).IsRequired();
        b.Property(v => v.Sha256).HasMaxLength(64).IsRequired();
        b.Property(v => v.UploadedByEmail).HasMaxLength(256);
        b.Property(v => v.Position).HasDefaultValue(1);
        b.HasIndex(v => new { v.DocumentId, v.VersionNumber }).IsUnique();
    }
}

public class DocumentReviewConfiguration : IEntityTypeConfiguration<DocumentReview>
{
    public void Configure(EntityTypeBuilder<DocumentReview> b)
    {
        b.ToTable("DocumentReviews");
        b.HasKey(r => r.Id);
        b.Property(r => r.Decision).HasConversion<string>().HasMaxLength(30);
        b.Property(r => r.Note).HasMaxLength(2000);
        b.Property(r => r.ReviewerEmail).HasMaxLength(256);
        b.HasIndex(r => new { r.DocumentId, r.ReviewedAtUtc });
        b.HasOne<CandidateDocument>().WithMany().HasForeignKey(r => r.DocumentId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class AdmissionOfferConfiguration : IEntityTypeConfiguration<AdmissionOffer>
{
    public void Configure(EntityTypeBuilder<AdmissionOffer> b)
    {
        b.ToTable("AdmissionOffers");
        b.HasKey(o => o.Id);
        b.Property(o => o.Conditions).HasMaxLength(2000);
        b.Property(o => o.RecordedByEmail).HasMaxLength(256);
        b.HasIndex(o => o.ApplicationId).IsUnique();
        b.HasOne<AdmissionApplication>().WithOne().HasForeignKey<AdmissionOffer>(o => o.ApplicationId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class AdmissionAcceptanceConfiguration : IEntityTypeConfiguration<AdmissionAcceptance>
{
    public void Configure(EntityTypeBuilder<AdmissionAcceptance> b)
    {
        b.ToTable("AdmissionAcceptances");
        b.HasKey(a => a.Id);
        b.Property(a => a.PalNumber).HasMaxLength(60);
        b.Property(a => a.Note).HasMaxLength(1000);
        b.Property(a => a.RecordedByEmail).HasMaxLength(256);
        b.HasIndex(a => a.ApplicationId).IsUnique();
        b.HasOne<AdmissionApplication>().WithOne().HasForeignKey<AdmissionAcceptance>(a => a.ApplicationId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class SchoolSuggestionConfiguration : IEntityTypeConfiguration<SchoolSuggestion>
{
    public void Configure(EntityTypeBuilder<SchoolSuggestion> b)
    {
        b.ToTable("SchoolSuggestions");
        b.HasKey(s => s.Id);
        b.Property(s => s.InstitutionName).HasMaxLength(200).IsRequired();
        b.Property(s => s.ProgrammeName).HasMaxLength(200).IsRequired();
        b.Property(s => s.Country).HasConversion<string>().HasMaxLength(20);
        b.Property(s => s.IntakeTerm).HasConversion<string>().HasMaxLength(20);
        b.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(s => s.Note).HasMaxLength(1000);
        b.Property(s => s.StaffNote).HasMaxLength(1000);
        b.Property(s => s.HandledByEmail).HasMaxLength(256);
        b.HasIndex(s => new { s.Status, s.CreatedAtUtc });
        b.HasOne<CandidateProfile>().WithMany().HasForeignKey(s => s.CandidateId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class EnquiryConfiguration : IEntityTypeConfiguration<Enquiry>
{
    public void Configure(EntityTypeBuilder<Enquiry> b)
    {
        b.ToTable("Enquiries");
        b.HasKey(e => e.Id);
        b.Property(e => e.Name).HasMaxLength(150).IsRequired();
        b.Property(e => e.Email).HasMaxLength(256);
        b.Property(e => e.Phone).HasMaxLength(30);
        b.Property(e => e.HighestQualification).HasConversion<string>().HasMaxLength(30);
        b.Property(e => e.DestinationInterest).HasConversion<string>().HasMaxLength(30);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.PreferredProgramme).HasMaxLength(200);
        b.Property(e => e.Message).HasMaxLength(2000);
        b.Property(e => e.StaffNote).HasMaxLength(2000);
        b.Property(e => e.HandledByEmail).HasMaxLength(256);
        b.Property(e => e.FirstViewedByEmail).HasMaxLength(256);
        b.HasIndex(e => new { e.Status, e.CreatedAtUtc });
    }
}

public class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> b)
    {
        b.ToTable("AuditEvents");
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).UseIdentityAlwaysColumn();
        // Stored by name: readable in SQL, and safe when new actions are added.
        b.Property(e => e.Action).HasConversion<string>().HasMaxLength(60);
        b.Property(e => e.Outcome).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.ActorEmail).HasMaxLength(256);
        b.Property(e => e.EntityType).HasMaxLength(60);
        b.Property(e => e.EntityId).HasMaxLength(64);
        b.Property(e => e.IpAddress).HasMaxLength(64);
        b.Property(e => e.Details).HasColumnType("jsonb");
        b.HasIndex(e => e.OccurredAtUtc);
        b.HasIndex(e => new { e.EntityType, e.EntityId });
        b.HasIndex(e => e.ActorUserId);
        // No foreign keys: audit history outlives the records it describes.
    }
}

public class AuthTokenConfiguration : IEntityTypeConfiguration<AuthToken>
{
    public void Configure(EntityTypeBuilder<AuthToken> b)
    {
        b.ToTable("AuthTokens");
        b.HasKey(t => t.Id);
        b.Property(t => t.Purpose).HasConversion<string>().HasMaxLength(30);
        b.Property(t => t.TokenHash).HasMaxLength(64).IsRequired();
        b.HasIndex(t => t.TokenHash).IsUnique();
        b.HasIndex(t => new { t.UserId, t.Purpose });
        b.HasOne<AppUser>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("RefreshTokens");
        b.HasKey(t => t.Id);
        b.Property(t => t.TokenHash).HasMaxLength(64).IsRequired();
        b.HasIndex(t => t.TokenHash).IsUnique();
        b.HasIndex(t => t.FamilyId);
        b.HasIndex(t => t.UserId);
        b.HasOne<AppUser>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class VisaCaseConfiguration : IEntityTypeConfiguration<VisaCase>
{
    public void Configure(EntityTypeBuilder<VisaCase> b)
    {
        b.ToTable("VisaCases");
        b.HasKey(v => v.Id);
        b.Property(v => v.Status).HasConversion<string>().HasMaxLength(40);
        b.Property(v => v.ReferenceNumber).HasMaxLength(100);
        b.Property(v => v.BiometricsLocation).HasMaxLength(300);
        b.HasIndex(v => v.ApplicationId).IsUnique();
        b.HasIndex(v => v.CandidateId);
        b.HasIndex(v => v.Status);
        b.HasOne<AdmissionApplication>().WithMany().HasForeignKey(v => v.ApplicationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CandidateProfile>().WithMany().HasForeignKey(v => v.CandidateId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class VisaStatusEventConfiguration : IEntityTypeConfiguration<VisaStatusEvent>
{
    public void Configure(EntityTypeBuilder<VisaStatusEvent> b)
    {
        b.ToTable("VisaStatusEvents");
        b.HasKey(e => e.Id);
        b.Property(e => e.From).HasConversion<string>().HasMaxLength(40);
        b.Property(e => e.To).HasConversion<string>().HasMaxLength(40);
        b.Property(e => e.RecordedByEmail).HasMaxLength(256);
        b.Property(e => e.Note).HasMaxLength(2000);
        b.HasIndex(e => new { e.VisaCaseId, e.RecordedAtUtc });
        b.HasIndex(e => new { e.To, e.OccurredOn });
        b.HasOne<VisaCase>().WithMany().HasForeignKey(e => e.VisaCaseId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class PreDepartureTemplateItemConfiguration : IEntityTypeConfiguration<PreDepartureTemplateItem>
{
    public void Configure(EntityTypeBuilder<PreDepartureTemplateItem> b)
    {
        b.ToTable("PreDepartureTemplate");
        b.HasKey(t => t.Id);
        b.Property(t => t.Country).HasConversion<string>().HasMaxLength(20);
        b.Property(t => t.Importance).HasConversion<string>().HasMaxLength(20);
        b.Property(t => t.Title).HasMaxLength(150).IsRequired();
        b.Property(t => t.Detail).HasMaxLength(500);
        b.HasIndex(t => new { t.Country, t.SortOrder });
    }
}

public class PreDepartureCaveatConfiguration : IEntityTypeConfiguration<PreDepartureCaveat>
{
    public void Configure(EntityTypeBuilder<PreDepartureCaveat> b)
    {
        b.ToTable("PreDepartureCaveats");
        b.HasKey(c => c.Id);
        b.Property(c => c.Country).HasConversion<string>().HasMaxLength(20);
        b.Property(c => c.Text).HasMaxLength(1500).IsRequired();
        b.HasIndex(c => c.Country).IsUnique();
    }
}

public class PreDepartureListConfiguration : IEntityTypeConfiguration<PreDepartureList>
{
    public void Configure(EntityTypeBuilder<PreDepartureList> b)
    {
        b.ToTable("PreDepartureLists");
        b.HasKey(l => l.Id);
        b.Property(l => l.ActivatedByEmail).HasMaxLength(256);
        b.HasIndex(l => l.VisaCaseId).IsUnique();
        b.HasOne<VisaCase>().WithOne().HasForeignKey<PreDepartureList>(l => l.VisaCaseId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class PreDepartureItemConfiguration : IEntityTypeConfiguration<PreDepartureItem>
{
    public void Configure(EntityTypeBuilder<PreDepartureItem> b)
    {
        b.ToTable("PreDepartureItems");
        b.HasKey(i => i.Id);
        b.Property(i => i.Importance).HasConversion<string>().HasMaxLength(20);
        b.Property(i => i.Source).HasConversion<string>().HasMaxLength(20);
        b.Property(i => i.Title).HasMaxLength(150).IsRequired();
        b.Property(i => i.Detail).HasMaxLength(500);
        b.Property(i => i.Note).HasMaxLength(500);
        b.Property(i => i.VerifiedByEmail).HasMaxLength(256);
        b.HasIndex(i => new { i.ListId, i.SortOrder });
        b.HasOne<PreDepartureList>().WithMany().HasForeignKey(i => i.ListId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class SentReminderConfiguration : IEntityTypeConfiguration<SentReminder>
{
    public void Configure(EntityTypeBuilder<SentReminder> b)
    {
        b.ToTable("SentReminders");
        b.HasKey(r => r.Id);
        b.Property(r => r.Kind).HasMaxLength(60).IsRequired();
        b.Property(r => r.SubjectKey).HasMaxLength(120).IsRequired();
        // This is the duplicate protection: one row per reminder, so a second claim fails.
        b.HasIndex(r => new { r.Kind, r.SubjectKey }).IsUnique();
    }
}

public class MessageLogConfiguration : IEntityTypeConfiguration<MessageLog>
{
    public void Configure(EntityTypeBuilder<MessageLog> b)
    {
        b.ToTable("MessageLogs");
        b.HasKey(m => m.Id);
        b.Property(m => m.Channel).HasConversion<string>().HasMaxLength(20);
        b.Property(m => m.State).HasConversion<string>().HasMaxLength(20);
        b.Property(m => m.Subject).HasMaxLength(200).IsRequired();
        b.Property(m => m.Recipient).HasMaxLength(120).IsRequired();
        b.Property(m => m.Error).HasMaxLength(300);
        b.HasIndex(m => m.AttemptedAtUtc);
    }
}

public class StaffTaskConfiguration: IEntityTypeConfiguration<StaffTask>
{
    public void Configure(EntityTypeBuilder<StaffTask> b)
    {
        b.ToTable("StaffTasks");
        b.HasKey(t => t.Id);
        b.Property(t => t.Title).HasMaxLength(200).IsRequired();
        b.Property(t => t.Notes).HasMaxLength(2000);
        b.Property(t => t.Priority).HasConversion<string>().HasMaxLength(20);
        b.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(t => t.CreatedByEmail).HasMaxLength(256);
        b.Property(t => t.CompletedByEmail).HasMaxLength(256);
        b.HasIndex(t => new { t.AssigneeUserId, t.Status, t.DueOn });
        b.HasIndex(t => t.CandidateId);
        b.HasOne<CandidateProfile>().WithMany().HasForeignKey(t => t.CandidateId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AdmissionApplication>().WithMany().HasForeignKey(t => t.ApplicationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<VisaCase>().WithMany().HasForeignKey(t => t.VisaCaseId).OnDelete(DeleteBehavior.Restrict);
    }
}
