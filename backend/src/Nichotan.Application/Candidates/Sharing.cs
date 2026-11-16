using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Nichotan.Application.Common;
using Nichotan.Domain.Audit;
using Nichotan.Domain.Candidates;
using Nichotan.Domain.Common;

namespace Nichotan.Application.Candidates;

/// <summary>
/// Staff opened WhatsApp with a list for a student ready to send. The browser opens WhatsApp itself, so the system can't know it was
/// sent; this records that it was opened, by whom and which list, because a student's list is going to an outside service.
/// </summary>
public record RecordWhatsAppShareCommand(Guid CandidateId, string Topic) : ICommand;

public class RecordWhatsAppShareValidator : AbstractValidator<RecordWhatsAppShareCommand>
{
    public static readonly string[] Topics = ["documents", "visa-documents", "pre-departure"];

    public RecordWhatsAppShareValidator() => RuleFor(x => x.Topic).Must(t => Topics.Contains(t)).WithMessage("Choose which list was shared.");
}

public class RecordWhatsAppShareHandler(IAppDbContext db, ICurrentUser current, IAuditLog audit) : ICommandHandler<RecordWhatsAppShareCommand>
{
    public async ValueTask<Unit> Handle(RecordWhatsAppShareCommand command, CancellationToken cancellationToken)
    {
        current.RequireAny(Guard.CaseWorkers);
        if (!await db.CandidateProfiles.AnyAsync(p => p.Id == command.CandidateId, cancellationToken)) throw new NotFoundException("Student not found.");
        audit.Add(AuditAction.WhatsAppShareOpened, entityType: nameof(CandidateProfile), entityId: command.CandidateId.ToString(), details: new { topic = command.Topic });
        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
