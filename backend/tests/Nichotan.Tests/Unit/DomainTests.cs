using AwesomeAssertions;
using Nichotan.Domain.Candidates;
using Nichotan.Domain.Common;
using Nichotan.Domain.Identity;

namespace Nichotan.Tests.Unit;

public class AuthTokenTests
{
    private static readonly DateTime Now = new(2026, 10, 26, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Only_the_hash_is_stored_and_the_link_works_once_before_it_expires()
    {
        var (token, plain) = AuthToken.Issue(Guid.NewGuid(), AuthTokenPurpose.PasswordReset, TimeSpan.FromHours(1), Now);

        token.TokenHash.Should().NotContain(plain).And.Be(AuthToken.Hash(plain));
        token.IsUsable(Now.AddMinutes(59)).Should().BeTrue();
        token.IsUsable(Now.AddHours(1)).Should().BeFalse();

        token.MarkUsed(Now);
        token.IsUsable(Now.AddMinutes(1)).Should().BeFalse();
    }

    [Fact]
    public void Tokens_are_random_and_url_safe()
    {
        var values = Enumerable.Range(0, 50).Select(_ => AuthToken.Issue(Guid.NewGuid(), AuthTokenPurpose.EmailVerification, TimeSpan.FromDays(1), Now).PlainValue).ToList();
        values.Should().OnlyHaveUniqueItems();
        values.Should().OnlyContain(v => v.Length >= 40 && v.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_'));
    }
}

public class RefreshTokenTests
{
    [Fact]
    public void Revoking_records_what_replaced_it_once()
    {
        var now = DateTime.UtcNow;
        var token = new RefreshToken(Guid.NewGuid(), "hash", Guid.NewGuid(), now.AddDays(14), mfaSatisfied: true);
        token.IsActive(now).Should().BeTrue();

        var next = Guid.NewGuid();
        token.Revoke(now, next);
        token.Revoke(now.AddMinutes(5), Guid.NewGuid());

        token.IsActive(now).Should().BeFalse();
        token.ReplacedById.Should().Be(next);
        token.RevokedAtUtc.Should().Be(now);
    }
}

public class CandidateProfileTests
{
    [Fact]
    public void Names_are_required_and_values_are_tidied()
    {
        var profile = new CandidateProfile(Guid.NewGuid(), "  Thandiwe ", " Dube", " Thandiwe@Example.COM ", "  ");
        profile.FullName.Should().Be("Thandiwe Dube");
        profile.Email.Should().Be("thandiwe@example.com");
        profile.Phone.Should().BeNull();

        var act = () => profile.Update(" ", "Dube", null);
        act.Should().Throw<DomainException>();
    }
}

public class RolesTests
{
    [Fact]
    public void Candidates_are_not_staff()
    {
        Roles.IsStaffRole(Roles.Candidate).Should().BeFalse();
        Roles.Staff.Should().OnlyContain(r => Roles.IsStaffRole(r)).And.HaveCount(2);
    }
}
