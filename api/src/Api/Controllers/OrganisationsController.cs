using System.Text;
using Api.Common;
using Api.Dtos;
using Api.Modules.Errors;
using Api.Services.Abstract;
using Application.Common.Permissions;
using Application.Organisations.Commands.AmendLocalAuthorityAllocation;
using Application.Organisations.Commands.BulkAmendLocalAuthorityAllocations;
using Application.Organisations.Commands.BulkImportLocalAuthorities;
using Application.Organisations.Commands.CreateLocalAuthority;
using Application.Organisations.Commands.DeleteLocalAuthority;
using Application.Organisations.Commands.ReinstateLocalAuthority;
using Application.Organisations.Commands.SuspendLocalAuthority;
using Application.Organisations.Commands.UpdateLocalAuthority;
using Domain.Organisations;
using Domain.ValueObjects;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[Route("organisations")]
[ApiController]
[Authorize(Roles = Role.Admin)]
public class OrganisationsController(
    PermissionsService permissionsService,
    ISender sender,
    IOrganisationsControllerService controllerService)
    : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<OrganisationDto>>> GetAll(CancellationToken cancellationToken)
    {
        var result = await controllerService.GetAll(cancellationToken);

        return result.ToList();
    }

    [HttpGet("{organisationId:guid}")]
    public async Task<ActionResult<OrganisationDto>> Get(
        [FromRoute] Guid organisationId,
        CancellationToken cancellationToken)
    {
        var result = await controllerService.Get(organisationId, cancellationToken);

        return result.Match<ActionResult<OrganisationDto>>(
            r => r,
            NotFound());
    }

    [HttpGet("{organisationId:guid}/totals")]
    public async Task<ActionResult<OrganisationHomeDto>> Totals(
        [FromRoute] Guid organisationId,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetOrganisationPermissions().MatchAsync<ActionResult<OrganisationHomeDto>>(
            async p =>
            {
                var result = await controllerService.Totals(p, organisationId, cancellationToken);
                return result.Match<ActionResult<OrganisationHomeDto>>(
                    r => r,
                    NotFound());
            },
            e => NotFound(e.Message));
    }

    [HttpGet("local-authorities")]
    public async Task<ActionResult<IReadOnlyList<LocalAuthorityDto>>> GetLocalAuthorities(
        [FromQuery] string? onboardingStatus,
        CancellationToken cancellationToken)
    {
        // An unrecognised value used to reach OnboardingStatus.From inside the repository's Where clause,
        // where it threw and surfaced as a 500 (BLUM-7240 AC4).
        QueryGuard.AllowedValue(
            onboardingStatus,
            OnboardingStatus.IsSupported,
            nameof(onboardingStatus),
            "Select a valid onboarding status.");

        var result = await controllerService.GetLocalAuthorities(onboardingStatus, cancellationToken);
        return result.ToList();
    }

    [HttpGet("local-authorities/{organisationId:guid}")]
    public async Task<ActionResult<LocalAuthorityDto>> GetLocalAuthority(
        [FromRoute] Guid organisationId,
        CancellationToken cancellationToken)
    {
        var result = await controllerService.GetLocalAuthority(organisationId, cancellationToken);
        return result.Match<ActionResult<LocalAuthorityDto>>(
            r => r,
            NotFound());
    }

    [HttpPost("local-authorities")]
    public async Task<ActionResult<LocalAuthorityDto>> CreateLocalAuthority(
        [FromBody] CreateLocalAuthorityRequestDto request,
        CancellationToken cancellationToken)
    {
        var command = new CreateLocalAuthorityCommand
        {
            Name = request.Name,
            OnsCode = request.OnsCode,
            Region = request.Region,
            Type = request.Type,
            ECode = request.ECode,
            AllocatedAmount = request.AllocatedAmount,
        };

        var result = await sender.Send(command, cancellationToken);
        return result.Match<ActionResult<LocalAuthorityDto>>(
            r => LocalAuthorityDto.FromDomainModel(r.Organisation, r.Allocation, null),
            error => error.ToObjectResult());
    }

    [HttpPut("local-authorities/{organisationId:guid}")]
    public async Task<ActionResult<LocalAuthorityDto>> UpdateLocalAuthority(
        [FromRoute] Guid organisationId,
        [FromBody] UpdateLocalAuthorityRequestDto request,
        CancellationToken cancellationToken)
    {
        var command = new UpdateLocalAuthorityCommand
        {
            Id = organisationId,
            Name = request.Name,
            OnsCode = request.OnsCode,
            Region = request.Region,
            Type = request.Type,
            ECode = request.ECode,
            OnboardingStatus = request.OnboardingStatus,
            ConfirmOnsCodeChange = request.ConfirmOnsCodeChange,
        };

        var result = await sender.Send(command, cancellationToken);
        return result.Match<ActionResult<LocalAuthorityDto>>(
            r => LocalAuthorityDto.FromDomainModel(r.Organisation, r.CurrentAllocation, null),
            error => error.ToObjectResult());
    }

    [HttpPatch("local-authorities/{organisationId:guid}/allocation")]
    public async Task<ActionResult<LocalAuthorityDto>> AmendLocalAuthorityAllocation(
        [FromRoute] Guid organisationId,
        [FromBody] AmendLocalAuthorityAllocationRequestDto request,
        CancellationToken cancellationToken)
    {
        var command = new AmendLocalAuthorityAllocationCommand
        {
            Id = organisationId,
            AllocatedAmount = request.AllocatedAmount,
        };

        var result = await sender.Send(command, cancellationToken);
        return result.Match<ActionResult<LocalAuthorityDto>>(
            r => LocalAuthorityDto.FromDomainModel(r.Organisation, r.Allocation, null),
            error => error.ToObjectResult());
    }

    [HttpPost("local-authorities/{organisationId:guid}/suspend")]
    public async Task<ActionResult<LocalAuthorityDto>> SuspendLocalAuthority(
        [FromRoute] Guid organisationId,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new SuspendLocalAuthorityCommand(organisationId), cancellationToken);
        return result.Match<ActionResult<LocalAuthorityDto>>(
            org => LocalAuthorityDto.FromDomainModel(org, null, null),
            error => error.ToObjectResult());
    }

    [HttpPost("local-authorities/{organisationId:guid}/reinstate")]
    public async Task<ActionResult<LocalAuthorityDto>> ReinstateLocalAuthority(
        [FromRoute] Guid organisationId,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ReinstateLocalAuthorityCommand(organisationId), cancellationToken);
        return result.Match<ActionResult<LocalAuthorityDto>>(
            org => LocalAuthorityDto.FromDomainModel(org, null, null),
            error => error.ToObjectResult());
    }

    [HttpDelete("local-authorities/{organisationId:guid}")]
    public async Task<ActionResult> DeleteLocalAuthority(
        [FromRoute] Guid organisationId,
        [FromQuery] bool acknowledgeActiveUsers,
        CancellationToken cancellationToken)
    {
        var command = new DeleteLocalAuthorityCommand
        {
            Id = organisationId,
            AcknowledgeActiveUsers = acknowledgeActiveUsers,
        };
        var result = await sender.Send(command, cancellationToken);
        return result.Match<ActionResult>(
            _ => NoContent(),
            error => error.ToObjectResult());
    }

    [HttpPost("local-authorities/bulk-import")]
    [RequestSizeLimit(20_000_000)]
    public async Task<ActionResult<BulkImportResultDto>> BulkImportLocalAuthorities(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        var command = new BulkImportLocalAuthoritiesCommand { File = file };
        var result = await sender.Send(command, cancellationToken);
        return result.Match<ActionResult<BulkImportResultDto>>(
            r => new BulkImportResultDto(
                r.CreatedCount,
                r.UpdatedCount,
                r.SkippedCount,
                r.FailedCount,
                r.Rows.Select(row => new BulkImportRowOutcomeDto(row.RowNumber, row.OnsCode, row.Name, row.Outcome.ToString(), row.Reason)).ToList()),
            error => error.ToObjectResult());
    }

    [HttpGet("local-authorities/bulk-import/template")]
    public ActionResult GetBulkImportTemplate()
    {
        // Built from the importer's own column list rather than restated here, so the template can never offer a
        // header the importer would reject (BLUM-7050 review).
        var csv = string.Join(',', BulkImportLocalAuthoritiesCommand.TemplateColumns) + "\n";
        var bytes = Encoding.UTF8.GetBytes(csv);
        return File(bytes, "text/csv", "local-authorities-template.csv");
    }

    [HttpPost("local-authorities/bulk-allocations")]
    [RequestSizeLimit(20_000_000)]
    public async Task<ActionResult<BulkImportResultDto>> BulkAmendLocalAuthorityAllocations(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        var command = new BulkAmendLocalAuthorityAllocationsCommand { File = file };
        var result = await sender.Send(command, cancellationToken);
        return result.Match<ActionResult<BulkImportResultDto>>(
            r => new BulkImportResultDto(
                r.CreatedCount,
                r.UpdatedCount,
                r.SkippedCount,
                r.FailedCount,
                r.Rows.Select(row => new BulkImportRowOutcomeDto(row.RowNumber, row.OnsCode, row.Name, row.Outcome.ToString(), row.Reason)).ToList()),
            error => error.ToObjectResult());
    }

    [HttpGet("local-authorities/bulk-allocations/template")]
    public ActionResult GetBulkAmendAllocationsTemplate()
    {
        var csv = "ons_code,allocated_amount\n";
        var bytes = Encoding.UTF8.GetBytes(csv);
        return File(bytes, "text/csv", "local-authorities-allocations-template.csv");
    }
}