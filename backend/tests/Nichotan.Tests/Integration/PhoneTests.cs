using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Nichotan.Application.Common;

namespace Nichotan.Tests.Integration;

public class PhoneNumberUnitTests
{
    [Theory]
    [InlineData("+263 77 123 4567", "+263771234567")]
    [InlineData("+1 (416) 555-0123", "+14165550123")]
    [InlineData("00263 77 123 4567", "+263771234567")]
    [InlineData("  +49-151-2345-6789 ", "+4915123456789")]
    public void An_international_number_is_stored_as_plus_and_digits(string typed, string expected) => PhoneNumbers.Normalise(typed).Should().Be(expected);

    [Theory]
    [InlineData("077 123 4567")]
    [InlineData("0771234567")]
    [InlineData("771234567")]
    [InlineData("+12")]
    [InlineData("+0771234567")]
    [InlineData("+1234567890123456")]
    [InlineData("call me maybe")]
    [InlineData("")]
    public void A_number_without_a_country_code_or_of_a_silly_length_is_not_accepted(string typed) => PhoneNumbers.Normalise(typed).Should().BeNull();
}

[Collection(ApiCollection.Name)]
public class PhoneTests(ApiFactory api)
{
    private static readonly int Year = DateTime.UtcNow.Year + 1;

    private async Task<HttpResponseMessage> RegisterAsync(string? phone) =>
        await api.NewClient().PostAsJsonAsync("/api/auth/register", new { firstName = "Pho", lastName = "Ne", email = Flows.NewEmail("phone"), password = "Candidate-Pass-2026", phone });

    [Fact]
    public async Task Registering_with_a_local_number_is_refused_with_clear_advice_and_a_good_number_is_stored_tidily()
    {
        var local = await RegisterAsync("077 123 4567");
        local.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await local.Content.ReadAsStringAsync()).Should().Contain("country code");

        (await RegisterAsync(null)).IsSuccessStatusCode.Should().BeTrue("the phone is optional");

        var email = Flows.NewEmail("phone");
        var created = await api.NewClient().PostAsJsonAsync("/api/auth/register", new { firstName = "Pho", lastName = "Ne", email, password = "Candidate-Pass-2026", phone = "+263 77 123 4567" });
        created.IsSuccessStatusCode.Should().BeTrue();
        var (client, _, _, _) = await Flows.RegisterCandidateAsync(api);
        var profile = await (await client.PutAsJsonAsync("/api/candidates/me", new { firstName = "A", lastName = "B", phone = "00263 71 612 4255" })).Read<ProfileDto>();
        profile.Phone.Should().Be("+263716124255", "spaces are tidied away and 00 becomes +, so it is ready for WhatsApp");
    }

    [Fact]
    public async Task Changing_my_details_needs_a_country_code_too_and_an_empty_number_clears_it()
    {
        var (client, _, _, _) = await Flows.RegisterCandidateAsync(api);
        var refused = await client.PutAsJsonAsync("/api/candidates/me", new { firstName = "A", lastName = "B", phone = "0716124255" });
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await refused.Content.ReadAsStringAsync()).Should().Contain("+263");

        await client.PutAsJsonAsync("/api/candidates/me", new { firstName = "A", lastName = "B", phone = "+1 416 555 0123" });
        var cleared = await (await client.PutAsJsonAsync("/api/candidates/me", new { firstName = "A", lastName = "B", phone = (string?)null })).Read<ProfileDto>();
        cleared.Phone.Should().BeNull();
    }

    [Fact]
    public async Task A_website_enquiry_phone_needs_a_country_code_but_email_only_is_still_fine()
    {
        object Enquiry(string? phone) => new { name = "Phone Enquirer", email = "ask@example.test", phone, highestQualification = "Diploma", graduationYear = DateTime.UtcNow.Year - 1, openToGuidance = true, destinationInterest = "Both" };
        var local = await api.NewClient().PostAsJsonAsync("/api/enquiries", Enquiry("0716124255"));
        local.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await local.Content.ReadAsStringAsync()).Should().Contain("country code");
        (await api.NewClient().PostAsJsonAsync("/api/enquiries", Enquiry("+263 71 612 4255"))).IsSuccessStatusCode.Should().BeTrue();
        (await api.NewClient().PostAsJsonAsync("/api/enquiries", Enquiry(null))).IsSuccessStatusCode.Should().BeTrue();
    }

    [Fact]
    public async Task The_application_form_can_be_drafted_with_any_number_but_not_signed_without_a_country_code()
    {
        var (student, _, _, _) = await Flows.RegisterCandidateAsync(api);
        object Form(string phone, string? kinPhone = null) => new
        {
            personal = new { firstName = "T", lastName = "C", gender = "Male", birthDate = "2004-05-17", citizenshipCountry = "Zimbabwe" },
            contact = new { homeAddress = "1 Rd", city = "Harare", country = "Zimbabwe", email = "t@example.test", phone, nextOfKinName = "R", nextOfKinEmail = "r@example.test", nextOfKinPhone = kinPhone },
            program = new { destinationCountry = "Canada", programLevel = "Bachelors", startYear = Year, startTerm = "Fall", institutions = new[] { "x" }, programChoices = new[] { "y" } },
            academic = new { highSchools = new[] { new { name = "H", provinceCountry = "Harare", started = "2016-01", completed = "2021-11", grade = "TwelveOrEquivalent" } }, postSecondary = Array.Empty<object>() },
        };
        var signature = "data:image/png;base64," + Convert.ToBase64String([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. new byte[300]]);
        Task<HttpResponseMessage> Submit() => student.PostAsJsonAsync("/api/application-form/submit", new { signerName = "T C", signatureImage = signature, acceptDeclaration = true, declarationVersion = "1" });

        // A draft saves whatever has been typed so far.
        (await student.PutAsJsonAsync("/api/application-form", Form("0771234567"))).StatusCode.Should().Be(HttpStatusCode.OK);
        var local = await Submit();
        local.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await local.Content.ReadAsStringAsync()).Should().Contain("country code");

        // The next of kin's number is checked the same way when it is given.
        await student.PutAsJsonAsync("/api/application-form", Form("+263771234567", "0712345678"));
        (await Submit()).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await student.PutAsJsonAsync("/api/application-form", Form("+263771234567", "+263 71 234 5678"));
        (await Submit()).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
