using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nichotan.Api.Security;
using Nichotan.Application.Admissions;
using Nichotan.Application.Candidates;
using Nichotan.Domain.Admissions;
using Nichotan.Domain.Applications;

namespace Nichotan.Api.Controllers;

public record SaveInstitutionRequest(string Name, DestinationCountry Country, string? Website, string? Notes);
public record SaveProgrammeRequest(Guid InstitutionId, string Name, ApplicantLevel Level, IReadOnlyList<IntakeDto> Intakes);
public record ArchiveRequest(bool Archived);
public record CreateAdmissionRequest(Guid CandidateId, Guid ProgrammeId, StartTerm IntakeTerm, int IntakeYear);
public record ChangeStatusRequest(AdmissionStatus ToStatus, DateOnly? OccurredOn, string? Note, string? InstitutionReference, string? PortalUrl, bool AcknowledgeDocumentGaps = false);
public record CorrectStatusRequest(AdmissionStatus ToStatus, DateOnly? OccurredOn, string Note);
public record CreateMyAdmissionRequest(Guid ProgrammeId, StartTerm IntakeTerm, int IntakeYear);
public record SuggestSchoolRequest(string InstitutionName, DestinationCountry Country, string ProgrammeName, StartTerm? IntakeTerm, int? IntakeYear, string? Note);
public record UpdateSuggestionRequest(SuggestionStatus Status, string? StaffNote);
public record AdmissionDetailsRequest(string? InstitutionReference, string? PortalUrl);

/// <summary>The institution and programme directory. Case workers read it; administrators and counsellors change it.</summary>
[ApiController]
[Route("api")]
public class DirectoryController(ISender sender) : ControllerBase
{
    [Authorize(Policy = Policies.CaseWorker)]
    [HttpGet("institutions")]
    public async Task<IReadOnlyList<InstitutionResult>> Institutions([FromQuery] DestinationCountry? country, [FromQuery] string? search, [FromQuery] bool includeArchived = false, CancellationToken cancellationToken = default) =>
        await sender.Send(new ListInstitutionsQuery(country, search, includeArchived), cancellationToken);

    [Authorize(Policy = Policies.AdmissionsStaff)]
    [HttpPost("institutions")]
    public async Task<InstitutionResult> CreateInstitution(SaveInstitutionRequest request, CancellationToken cancellationToken) =>
        await sender.Send(new SaveInstitutionCommand(null, request.Name, request.Country, request.Website, request.Notes), cancellationToken);

    [Authorize(Policy = Policies.AdmissionsStaff)]
    [HttpPut("institutions/{id:guid}")]
    public async Task<InstitutionResult> UpdateInstitution(Guid id, SaveInstitutionRequest request, CancellationToken cancellationToken) =>
        await sender.Send(new SaveInstitutionCommand(id, request.Name, request.Country, request.Website, request.Notes), cancellationToken);

    [Authorize(Policy = Policies.AdmissionsStaff)]
    [HttpPost("institutions/{id:guid}/archive")]
    public async Task<InstitutionResult> ArchiveInstitution(Guid id, ArchiveRequest request, CancellationToken cancellationToken) =>
        await sender.Send(new SetInstitutionArchivedCommand(id, request.Archived), cancellationToken);

    [Authorize(Policy = Policies.CaseWorker)]
    [HttpGet("programmes")]
    public async Task<IReadOnlyList<ProgrammeResult>> Programmes([FromQuery] Guid? institutionId, [FromQuery] ApplicantLevel? level, [FromQuery] DestinationCountry? country, [FromQuery] bool includeArchived = false, CancellationToken cancellationToken = default) =>
        await sender.Send(new ListProgrammesQuery(institutionId, level, country, includeArchived), cancellationToken);

    [Authorize(Policy = Policies.AdmissionsStaff)]
    [HttpPost("programmes")]
    public async Task<ProgrammeResult> CreateProgramme(SaveProgrammeRequest request, CancellationToken cancellationToken) =>
        await sender.Send(new SaveProgrammeCommand(null, request.InstitutionId, request.Name, request.Level, request.Intakes), cancellationToken);

    [Authorize(Policy = Policies.AdmissionsStaff)]
    [HttpPut("programmes/{id:guid}")]
    public async Task<ProgrammeResult> UpdateProgramme(Guid id, SaveProgrammeRequest request, CancellationToken cancellationToken) =>
        await sender.Send(new SaveProgrammeCommand(id, request.InstitutionId, request.Name, request.Level, request.Intakes), cancellationToken);

    [Authorize(Policy = Policies.AdmissionsStaff)]
    [HttpPost("programmes/{id:guid}/archive")]
    public async Task<ProgrammeResult> ArchiveProgramme(Guid id, ArchiveRequest request, CancellationToken cancellationToken) =>
        await sender.Send(new SetProgrammeArchivedCommand(id, request.Archived), cancellationToken);
}

/// <summary>Admission applications. Statuses are entered by staff from the school's own portal; nothing syncs.</summary>
[ApiController]
[Route("api/admissions")]
public class AdmissionsController(ISender sender) : ControllerBase
{
    [Authorize(Policy = Policies.CaseWorker)]
    [HttpGet]
    public async Task<PagedResult<AdmissionSummary>> List([FromQuery] AdmissionStatus? status, [FromQuery] DestinationCountry? country, [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default) =>
        await sender.Send(new ListAdmissionsQuery(status, country, search, page, pageSize), cancellationToken);

    /// <summary>Counts for the dashboard: open applications now, offers received, and all applications ever.</summary>
    [Authorize(Policy = Policies.CaseWorker)]
    [HttpGet("stats")]
    public async Task<AdmissionStats> Stats(CancellationToken cancellationToken) => await sender.Send(new GetAdmissionStatsQuery(), cancellationToken);

    /// <summary>Applications where the deposit is verified but the PAL hasn't arrived yet, longest-waiting first.</summary>
    [Authorize(Policy = Policies.CaseWorker)]
    [HttpGet("pal-follow-ups")]
    public async Task<IReadOnlyList<PalFollowUp>> PalFollowUps(CancellationToken cancellationToken) => await sender.Send(new ListPalFollowUpsQuery(), cancellationToken);

    [Authorize(Policy = Policies.CaseWorker)]
    [HttpGet("candidate/{candidateId:guid}")]
    public async Task<IReadOnlyList<AdmissionSummary>> ForCandidate(Guid candidateId, CancellationToken cancellationToken) =>
        await sender.Send(new ListCandidateAdmissionsQuery(candidateId), cancellationToken);

    [Authorize(Policy = Policies.CaseWorker)]
    [HttpGet("{id:guid}")]
    public async Task<AdmissionDetail> Get(Guid id, CancellationToken cancellationToken) => await sender.Send(new GetAdmissionQuery(id), cancellationToken);

    [Authorize(Policy = Policies.AdmissionsStaff)]
    [HttpPost]
    public async Task<AdmissionDetail> Create(CreateAdmissionRequest request, CancellationToken cancellationToken) =>
        await sender.Send(new CreateAdmissionCommand(request.CandidateId, request.ProgrammeId, request.IntakeTerm, request.IntakeYear), cancellationToken);

    [Authorize(Policy = Policies.AdmissionsStaff)]
    [HttpPost("{id:guid}/status")]
    public async Task<AdmissionDetail> ChangeStatus(Guid id, ChangeStatusRequest request, CancellationToken cancellationToken) =>
        await sender.Send(new ChangeAdmissionStatusCommand(id, request.ToStatus, request.OccurredOn, request.Note, request.InstitutionReference, request.PortalUrl, request.AcknowledgeDocumentGaps), cancellationToken);

    [Authorize(Policy = Policies.Administrator)]
    [HttpPost("{id:guid}/correction")]
    public async Task<AdmissionDetail> Correct(Guid id, CorrectStatusRequest request, CancellationToken cancellationToken) =>
        await sender.Send(new CorrectAdmissionStatusCommand(id, request.ToStatus, request.OccurredOn, request.Note), cancellationToken);

    [Authorize(Policy = Policies.AdmissionsStaff)]
    [HttpPut("{id:guid}/details")]
    public async Task<AdmissionDetail> Details(Guid id, AdmissionDetailsRequest request, CancellationToken cancellationToken) =>
        await sender.Send(new UpdateAdmissionDetailsCommand(id, request.InstitutionReference, request.PortalUrl), cancellationToken);

    /// <summary>A student adds an application for themselves, from the directory. Staff then review it and submit it on the school's portal.</summary>
    [Authorize(Policy = Policies.Candidate)]
    [HttpPost("mine")]
    public async Task<MyAdmissionResult> CreateMine(CreateMyAdmissionRequest request, CancellationToken cancellationToken) =>
        await sender.Send(new CreateMyAdmissionCommand(request.ProgrammeId, request.IntakeTerm, request.IntakeYear), cancellationToken);

    [Authorize(Policy = Policies.Candidate)]
    [HttpPost("mine/{id:guid}/send")]
    public async Task<MyAdmissionResult> SendMine(Guid id, CancellationToken cancellationToken) => await sender.Send(new SendMyAdmissionCommand(id), cancellationToken);

    [Authorize(Policy = Policies.Candidate)]
    [HttpPost("mine/{id:guid}/withdraw")]
    public async Task<MyAdmissionResult> WithdrawMine(Guid id, CancellationToken cancellationToken) => await sender.Send(new WithdrawMyAdmissionCommand(id), cancellationToken);

    /// <summary>The schools and programmes a student can choose from.</summary>
    [Authorize(Policy = Policies.Candidate)]
    [HttpGet("directory")]
    public async Task<IReadOnlyList<DirectoryInstitution>> StudentDirectory(CancellationToken cancellationToken) => await sender.Send(new GetStudentDirectoryQuery(), cancellationToken);

    /// <summary>A student asks for a school that isn't listed.</summary>
    [Authorize(Policy = Policies.Candidate)]
    [HttpPost("suggestions")]
    public async Task<SuggestionResult> Suggest(SuggestSchoolRequest request, CancellationToken cancellationToken) =>
        await sender.Send(new SuggestSchoolCommand(request.InstitutionName, request.Country, request.ProgrammeName, request.IntakeTerm, request.IntakeYear, request.Note), cancellationToken);

    [Authorize(Policy = Policies.Candidate)]
    [HttpGet("suggestions/mine")]
    public async Task<IReadOnlyList<SuggestionResult>> MySuggestions(CancellationToken cancellationToken) => await sender.Send(new ListMySuggestionsQuery(), cancellationToken);

    [Authorize(Policy = Policies.CaseWorker)]
    [HttpGet("suggestions")]
    public async Task<IReadOnlyList<SuggestionResult>> Suggestions([FromQuery] SuggestionStatus? status, CancellationToken cancellationToken) => await sender.Send(new ListSuggestionsQuery(status), cancellationToken);

    [Authorize(Policy = Policies.AdmissionsStaff)]
    [HttpPut("suggestions/{id:guid}")]
    public async Task<SuggestionResult> UpdateSuggestion(Guid id, UpdateSuggestionRequest request, CancellationToken cancellationToken) =>
        await sender.Send(new UpdateSuggestionCommand(id, request.Status, request.StaffNote), cancellationToken);

    /// <summary>The signed-in candidate's own applications, without staff notes.</summary>
    [Authorize(Policy = Policies.Candidate)]
    [HttpGet("mine")]
    public async Task<IReadOnlyList<MyAdmissionResult>> Mine(CancellationToken cancellationToken) => await sender.Send(new GetMyAdmissionsQuery(), cancellationToken);
}
