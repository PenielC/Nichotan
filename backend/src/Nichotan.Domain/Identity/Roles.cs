namespace Nichotan.Domain.Identity;

/// <summary>
/// Three kinds of user: administrators, counsellors (the staff who work with students) and students. Role names are stored as
/// these exact strings. "AdmissionsCounsellor" is the counsellor role; it covers admissions, documents, visas and reports.
/// </summary>
public static class Roles
{
    public const string Administrator = "Administrator";
    public const string AdmissionsCounsellor = "AdmissionsCounsellor";
    public const string Candidate = "Candidate";

    /// <summary>Roles an administrator can give to staff accounts.</summary>
    public static readonly IReadOnlyList<string> Staff = [Administrator, AdmissionsCounsellor];

    public static readonly IReadOnlyList<string> All = [.. Staff, Candidate];

    public static bool IsStaffRole(string role) => Staff.Contains(role);
}
