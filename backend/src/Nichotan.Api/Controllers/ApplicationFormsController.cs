using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nichotan.Api.Security;
using Nichotan.Application.Applications;
using Nichotan.Application.Candidates;
using Nichotan.Domain.Applications;

namespace Nichotan.Api.Controllers;

public record SubmitApplicationRequest(string SignerName, string? SignatureImage, bool AcceptDeclaration, string DeclarationVersion);
public record ReopenFormRequest(string Reason);

/// <summary>The signed-in candidate's own Nichotan application form. No endpoint here takes a candidate id.</summary>
[ApiController]
[Route("api/application-form")]
[Authorize(Policy = Policies.Candidate)]
public class MyApplicationFormController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ApplicationFormResult> Get(CancellationToken cancellationToken) => await sender.Send(new GetMyApplicationFormQuery(), cancellationToken);

    /// <summary>Saves the draft. Works only while the form is a draft or has been reopened.</summary>
    [HttpPut]
    public async Task<ApplicationFormResult> Save(ApplicationFormData data, CancellationToken cancellationToken) =>
        await sender.Send(new SaveMyApplicationDraftCommand(data), cancellationToken);

    /// <summary>Signs and submits the saved draft; the form is locked afterwards.</summary>
    [HttpPost("submit")]
    public async Task<ApplicationFormResult> Submit(SubmitApplicationRequest request, CancellationToken cancellationToken) =>
        await sender.Send(new SubmitMyApplicationCommand(request.SignerName, request.SignatureImage, request.AcceptDeclaration, request.DeclarationVersion), cancellationToken);

    [HttpGet("versions/{versionNumber:int}")]
    public async Task<FormVersionResult> Version(int versionNumber, CancellationToken cancellationToken) =>
        await sender.Send(new GetMyFormVersionQuery(versionNumber), cancellationToken);

    /// <summary>The signed form as a printable PDF.</summary>
    [HttpGet("versions/{versionNumber:int}/pdf")]
    public async Task<IActionResult> Pdf(int versionNumber, CancellationToken cancellationToken)
    {
        var pdf = await sender.Send(new GetMyFormPdfQuery(versionNumber), cancellationToken);
        return File(pdf.Content, "application/pdf", pdf.FileName);
    }
}

/// <summary>Staff access to candidates' forms.</summary>
[ApiController]
[Route("api/application-forms")]
[Authorize(Policy = Policies.CaseWorker)]
public class ApplicationFormsController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<PagedResult<ApplicationFormSummary>> List([FromQuery] FormStatus? status, [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default) =>
        await sender.Send(new ListApplicationFormsQuery(status, search, page, pageSize), cancellationToken);

    [HttpGet("{candidateId:guid}")]
    public async Task<ApplicationFormResult> Get(Guid candidateId, CancellationToken cancellationToken) => await sender.Send(new GetApplicationFormQuery(candidateId), cancellationToken);

    [HttpGet("{candidateId:guid}/versions/{versionNumber:int}")]
    public async Task<FormVersionResult> Version(Guid candidateId, int versionNumber, CancellationToken cancellationToken) =>
        await sender.Send(new GetApplicationFormVersionQuery(candidateId, versionNumber), cancellationToken);

    [HttpGet("{candidateId:guid}/versions/{versionNumber:int}/pdf")]
    public async Task<IActionResult> Pdf(Guid candidateId, int versionNumber, CancellationToken cancellationToken)
    {
        var pdf = await sender.Send(new GetApplicationFormPdfQuery(candidateId, versionNumber), cancellationToken);
        return File(pdf.Content, "application/pdf", pdf.FileName);
    }

    /// <summary>Sends a submitted form back to the candidate for corrections.</summary>
    [HttpPost("{candidateId:guid}/reopen")]
    public async Task<ApplicationFormResult> Reopen(Guid candidateId, ReopenFormRequest request, CancellationToken cancellationToken) =>
        await sender.Send(new ReopenApplicationFormCommand(candidateId, request.Reason), cancellationToken);
}
