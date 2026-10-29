using Api.Common;
using Api.Dtos;
using Api.Modules.Errors;
using Api.Services.Abstract;
using Application.Common.Features;
using Application.Common.Interfaces.Queries;
using Application.DataCollectionForms.Commands;
using Application.DataCollectionForms.Commands.CreateFormModule;
using Application.DataCollectionForms.Commands.CreateFormQuestion;
using Application.DataCollectionForms.Commands.CreateFormSection;
using Application.DataCollectionForms.Commands.DeleteFormModule;
using Application.DataCollectionForms.Commands.DeleteFormQuestion;
using Application.DataCollectionForms.Commands.DeleteFormSection;
using Application.DataCollectionForms.Commands.PublishFormModule;
using Application.DataCollectionForms.Commands.ReorderFormQuestions;
using Application.DataCollectionForms.Commands.ReorderFormSections;
using Application.DataCollectionForms.Commands.RestoreFormModuleVersion;
using Application.DataCollectionForms.Commands.StartFormModuleDraft;
using Application.DataCollectionForms.Commands.UpdateFormModule;
using Application.DataCollectionForms.Commands.UpdateFormQuestion;
using Application.DataCollectionForms.Commands.UpdateFormSection;
using Application.DataCollectionForms.Exceptions;
using Domain.DataCollectionForms;
using Domain.ValueObjects;
using LanguageExt.UnsafeValueAccess;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

// DHSC/Departmental admin forms-builder authoring surface for reusable data-collection form modules
// (sections + questions). Modules are authored as drafts and locked once published.
[ApiController]
[Route("admin/data-collection-forms")]
[Authorize(Roles = Role.Admin)]
public class DataCollectionFormsController(
    ISender sender,
    IDataCollectionFormsControllerService controllerService,
    IFormModuleQueries formModuleQueries)
    : ControllerBase
{
    [HttpGet("modules")]
    public async Task<ActionResult<IReadOnlyList<FormModuleDto>>> ListModules(CancellationToken cancellationToken)
    {
        var modules = await controllerService.ListAsync(cancellationToken);
        return Ok(modules);
    }

    [HttpGet("modules/{moduleId:guid}")]
    public async Task<ActionResult<FormModuleWithChildrenDto>> GetModule(
        [FromRoute] Guid moduleId,
        CancellationToken cancellationToken)
    {
        var lookup = await formModuleQueries.GetByIdWithChildrenAsync(new FormModuleId(moduleId), cancellationToken);
        if (lookup.IsNone)
        {
            return new FormModuleNotFoundException(new FormModuleId(moduleId)).ToObjectResult();
        }

        return controllerService.ToDto(lookup.ValueUnsafe());
    }

    [HttpPost("modules")]
    public async Task<ActionResult<FormModuleWithChildrenDto>> CreateModule(
        [FromBody] CreateFormModuleRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new CreateFormModuleCommand
            {
                Code = request.Code,
                Name = request.Name,
                Description = request.Description,
            },
            cancellationToken);

        return result.Match<ActionResult<FormModuleWithChildrenDto>>(
            module => controllerService.ToDto(module),
            error => error.ToObjectResult());
    }

    [HttpPut("modules/{moduleId:guid}")]
    public async Task<ActionResult<FormModuleWithChildrenDto>> UpdateModule(
        [FromRoute] Guid moduleId,
        [FromBody] UpdateFormModuleRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new UpdateFormModuleCommand
            {
                Id = moduleId,
                Name = request.Name,
                Description = request.Description,
            },
            cancellationToken);

        return result.Match<ActionResult<FormModuleWithChildrenDto>>(
            module => controllerService.ToDto(module),
            error => error.ToObjectResult());
    }

    [HttpDelete("modules/{moduleId:guid}")]
    public async Task<ActionResult> DeleteModule(
        [FromRoute] Guid moduleId,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new DeleteFormModuleCommand { Id = moduleId }, cancellationToken);
        return result.Match<ActionResult>(_ => NoContent(), error => error.ToObjectResult());
    }

    [HttpPost("modules/{moduleId:guid}/publish")]
    public async Task<ActionResult<FormModuleWithChildrenDto>> PublishModule(
        [FromRoute] Guid moduleId,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new PublishFormModuleCommand { Id = moduleId }, cancellationToken);
        return result.Match<ActionResult<FormModuleWithChildrenDto>>(
            published => controllerService.ToDto(published.Module) with
            {
                LatestVersionNumber = published.LatestVersionNumber,
                VersionCreated = published.LatestVersionNumber is null ? null : published.VersionCreated,
            },
            error => error.ToObjectResult());
    }

    // BLUM-9102: reopens a published module for editing; its published content is already kept as a version.
    [HttpPost("modules/{moduleId:guid}/draft")]
    [RequiresFeature(Feature.BLUM_9087)]
    public async Task<ActionResult<FormModuleWithChildrenDto>> StartDraft(
        [FromRoute] Guid moduleId,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new StartFormModuleDraftCommand { Id = moduleId }, cancellationToken);
        return result.Match<ActionResult<FormModuleWithChildrenDto>>(
            module => controllerService.ToDto(module),
            error => error.ToObjectResult());
    }

    // BLUM-9102: the module's saved versions, newest first. An unknown module has none, so it lists nothing.
    [HttpGet("modules/{moduleId:guid}/versions")]
    [RequiresFeature(Feature.BLUM_9087)]
    public async Task<ActionResult<IReadOnlyList<FormModuleVersionSummaryDto>>> ListVersions(
        [FromRoute] Guid moduleId,
        CancellationToken cancellationToken)
    {
        return Ok(await controllerService.ListVersionsAsync(FormModuleId.From(moduleId), cancellationToken));
    }

    // BLUM-9086: the collections that use each of the module's versions, newest version first. Soft-deleted collections
    // are left out. An unknown module has no versions, so it lists nothing.
    [HttpGet("modules/{moduleId:guid}/versions/collections")]
    [RequiresFeature(Feature.BLUM_9087)]
    public async Task<ActionResult<IReadOnlyList<FormModuleVersionCollectionsDto>>> ListVersionCollections(
        [FromRoute] Guid moduleId,
        CancellationToken cancellationToken)
    {
        return Ok(await controllerService.ListVersionCollectionsAsync(FormModuleId.From(moduleId), cancellationToken));
    }

    [HttpGet("modules/{moduleId:guid}/versions/{versionNumber:int}")]
    [RequiresFeature(Feature.BLUM_9087)]
    public async Task<ActionResult<FormModuleVersionDto>> GetVersion(
        [FromRoute] Guid moduleId,
        [FromRoute] int versionNumber,
        CancellationToken cancellationToken)
    {
        var version = await controllerService.GetVersionAsync(FormModuleId.From(moduleId), versionNumber, cancellationToken);
        return version.Match<ActionResult<FormModuleVersionDto>>(
            dto => dto,
            () => new FormModuleVersionNotFoundException(FormModuleId.From(moduleId), versionNumber).ToObjectResult());
    }

    // BLUM-9102: replaces the draft with what a saved version held (a published module is reopened first).
    // Publishing it afterwards saves the next version; the restored version itself never changes.
    [HttpPost("modules/{moduleId:guid}/versions/{versionNumber:int}/restore")]
    [RequiresFeature(Feature.BLUM_9087)]
    public async Task<ActionResult<FormModuleWithChildrenDto>> RestoreVersion(
        [FromRoute] Guid moduleId,
        [FromRoute] int versionNumber,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new RestoreFormModuleVersionCommand { ModuleId = moduleId, VersionNumber = versionNumber }, cancellationToken);
        return result.Match<ActionResult<FormModuleWithChildrenDto>>(
            module => controllerService.ToDto(module),
            error => error.ToObjectResult());
    }

    [HttpPost("modules/{moduleId:guid}/sections")]
    public async Task<ActionResult<FormModuleWithChildrenDto>> CreateSection(
        [FromRoute] Guid moduleId,
        [FromBody] CreateFormSectionRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new CreateFormSectionCommand
            {
                FormModuleId = moduleId,
                Title = request.Title,
                Description = request.Description,
                HelpText = request.HelpText,
            },
            cancellationToken);

        return result.Match<ActionResult<FormModuleWithChildrenDto>>(
            module => controllerService.ToDto(module),
            error => error.ToObjectResult());
    }

    [HttpPut("modules/{moduleId:guid}/sections/{sectionId:guid}")]
    public async Task<ActionResult<FormModuleWithChildrenDto>> UpdateSection(
        [FromRoute] Guid moduleId,
        [FromRoute] Guid sectionId,
        [FromBody] UpdateFormSectionRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new UpdateFormSectionCommand
            {
                FormModuleId = moduleId,
                SectionId = sectionId,
                Title = request.Title,
                Description = request.Description,
                HelpText = request.HelpText,
            },
            cancellationToken);

        return result.Match<ActionResult<FormModuleWithChildrenDto>>(
            module => controllerService.ToDto(module),
            error => error.ToObjectResult());
    }

    [HttpDelete("modules/{moduleId:guid}/sections/{sectionId:guid}")]
    public async Task<ActionResult<FormModuleWithChildrenDto>> DeleteSection(
        [FromRoute] Guid moduleId,
        [FromRoute] Guid sectionId,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new DeleteFormSectionCommand { FormModuleId = moduleId, SectionId = sectionId },
            cancellationToken);

        return result.Match<ActionResult<FormModuleWithChildrenDto>>(
            module => controllerService.ToDto(module),
            error => error.ToObjectResult());
    }

    [HttpPost("modules/{moduleId:guid}/sections/reorder")]
    public async Task<ActionResult<FormModuleWithChildrenDto>> ReorderSections(
        [FromRoute] Guid moduleId,
        [FromBody] ReorderRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new ReorderFormSectionsCommand { FormModuleId = moduleId, OrderedSectionIds = request.OrderedIds ?? [] },
            cancellationToken);

        return result.Match<ActionResult<FormModuleWithChildrenDto>>(
            module => controllerService.ToDto(module),
            error => error.ToObjectResult());
    }

    [HttpPost("modules/{moduleId:guid}/questions")]
    public async Task<ActionResult<FormModuleWithChildrenDto>> CreateQuestion(
        [FromRoute] Guid moduleId,
        [FromBody] CreateFormQuestionRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new CreateFormQuestionCommand
            {
                FormModuleId = moduleId,
                SectionId = request.SectionId,
                Code = request.Code,
                Label = request.Label,
                QuestionType = request.QuestionType,
                IsRequired = request.IsRequired,
                Hint = request.Hint,
                HelpText = request.HelpText,
                Options = MapOptions(request.Options),
            },
            cancellationToken);

        return result.Match<ActionResult<FormModuleWithChildrenDto>>(
            module => controllerService.ToDto(module),
            error => error.ToObjectResult());
    }

    [HttpPut("modules/{moduleId:guid}/questions/{questionId:guid}")]
    public async Task<ActionResult<FormModuleWithChildrenDto>> UpdateQuestion(
        [FromRoute] Guid moduleId,
        [FromRoute] Guid questionId,
        [FromBody] UpdateFormQuestionRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new UpdateFormQuestionCommand
            {
                FormModuleId = moduleId,
                Id = questionId,
                SectionId = request.SectionId,
                Label = request.Label,
                QuestionType = request.QuestionType,
                IsRequired = request.IsRequired,
                Hint = request.Hint,
                HelpText = request.HelpText,
                Options = MapOptions(request.Options),
            },
            cancellationToken);

        return result.Match<ActionResult<FormModuleWithChildrenDto>>(
            module => controllerService.ToDto(module),
            error => error.ToObjectResult());
    }

    [HttpDelete("modules/{moduleId:guid}/questions/{questionId:guid}")]
    public async Task<ActionResult<FormModuleWithChildrenDto>> DeleteQuestion(
        [FromRoute] Guid moduleId,
        [FromRoute] Guid questionId,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new DeleteFormQuestionCommand { FormModuleId = moduleId, Id = questionId },
            cancellationToken);

        return result.Match<ActionResult<FormModuleWithChildrenDto>>(
            module => controllerService.ToDto(module),
            error => error.ToObjectResult());
    }

    [HttpPost("modules/{moduleId:guid}/questions/reorder")]
    public async Task<ActionResult<FormModuleWithChildrenDto>> ReorderQuestions(
        [FromRoute] Guid moduleId,
        [FromBody] ReorderRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new ReorderFormQuestionsCommand { FormModuleId = moduleId, OrderedQuestionIds = request.OrderedIds ?? [] },
            cancellationToken);

        return result.Match<ActionResult<FormModuleWithChildrenDto>>(
            module => controllerService.ToDto(module),
            error => error.ToObjectResult());
    }

    private static IReadOnlyList<QuestionOptionInput> MapOptions(IReadOnlyList<FormModuleQuestionOptionRequestDto>? options) =>
        options?.Select(o => new QuestionOptionInput(o.Value, o.Label)).ToList() ?? [];
}