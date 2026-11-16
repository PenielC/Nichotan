using Nichotan.Domain.Common;

namespace Nichotan.Domain.Candidates;

/// <summary>
/// The person applying through Nichotan. Deliberately separate from any admission application: one
/// candidate can apply to several institutions (BRD 6.2). The full personal details from the Nichotan
/// form arrive in milestone M2.
/// </summary>
public class CandidateProfile : Entity
{
    private CandidateProfile() { }

    public CandidateProfile(Guid userId, string firstName, string lastName, string email, string? phone)
    {
        UserId = userId;
        Email = email.Trim().ToLowerInvariant();
        Update(firstName, lastName, phone);
    }

    /// <summary>The sign-in account this profile belongs to.</summary>
    public Guid UserId { get; private set; }

    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string? Phone { get; private set; }

    public string FullName => $"{FirstName} {LastName}";

    public void Update(string firstName, string lastName, string? phone)
    {
        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
        {
            throw new DomainException("First and last name are required.");
        }

        FirstName = firstName.Trim();
        LastName = lastName.Trim();
        Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
    }
}
