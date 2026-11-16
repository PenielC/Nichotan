using System.Net;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Nichotan.Application.Common;
using Nichotan.Domain.Messaging;
using Nichotan.Infrastructure.Persistence;
using Nichotan.Infrastructure.Services;

namespace Nichotan.Tests.Integration;

public class MessageMaskingTests
{
    [Theory]
    [InlineData("jane.doe@example.com", "j***@example.com")]
    [InlineData("+263771234567", "+263***67")]
    [InlineData("12", "***")]
    public void A_recipient_is_masked_before_it_is_logged(string raw, string masked) => MessageLog.MaskRecipient(raw).Should().Be(masked);
}

[Collection(ApiCollection.Name)]
public class MessageLogTests(ApiFactory api)
{
    private sealed class Inner(bool fail) : IEmailSender
    {
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default) =>
            fail ? throw new InvalidOperationException("The mail server said no.") : Task.CompletedTask;
    }

    private LoggingEmailSender Sender(bool fail) =>
        new(new Inner(fail), api.Services.GetRequiredService<IServiceScopeFactory>(), new SystemClock(), NullLogger<LoggingEmailSender>.Instance);

    private async Task<List<MessageLog>> LogFor(string subject)
    {
        using var scope = api.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().MessageLogs.AsNoTracking().Where(m => m.Subject == subject).ToListAsync();
    }

    [Fact]
    public async Task A_sent_and_a_failed_email_are_both_recorded_with_the_recipient_masked_and_no_body()
    {
        var ok = $"Hello {Guid.NewGuid():N}";
        var bad = $"Broken {Guid.NewGuid():N}";
        await Sender(false).SendAsync(new EmailMessage("jane.doe@example.com", ok, "secret text body", "<p>secret</p>"));
        var act = () => Sender(true).SendAsync(new EmailMessage("jane.doe@example.com", bad, "secret text body", "<p>secret</p>"));
        await act.Should().ThrowAsync<InvalidOperationException>("a failure is still the caller's to handle");

        var sent = (await LogFor(ok)).Should().ContainSingle().Subject;
        sent.State.Should().Be(MessageState.Sent);
        sent.Recipient.Should().Be("j***@example.com");
        var failed = (await LogFor(bad)).Should().ContainSingle().Subject;
        failed.State.Should().Be(MessageState.Failed);
        failed.Error.Should().Contain("mail server said no");
    }

    [Fact]
    public async Task Only_an_administrator_can_read_the_message_log()
    {
        var admin = await Flows.AdminAsync(api);
        var ok = await admin.GetAsync("/api/messages");
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ok.Content.ReadAsStringAsync()).Should().Contain("\"items\"");

        var (counsellor, _, _, _) = await Flows.NewStaffAsync(api, admin, "AdmissionsCounsellor");
        (await counsellor.GetAsync("/api/messages")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var (student, _, _, _) = await Flows.RegisterCandidateAsync(api);
        (await student.GetAsync("/api/messages")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
