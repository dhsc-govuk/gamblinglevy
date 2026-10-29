using Api.Common;
using Api.Dtos;
using Api.Modules.Errors;
using Api.Services.Abstract;
using Application.Common.Features;
using Application.Common.Interfaces.Queries;
using Application.Forms.Commands;
using Application.Forms.Commands.CreateQuestion;
using Application.Forms.Commands.CreateSection;
using Application.Forms.Commands.DeleteQuestion;
using Application.Forms.Commands.DeleteSection;
using Application.Forms.Commands.ReorderQuestions;
using Application.Forms.Commands.ReorderSections;
using Application.Forms.Commands.UpdateQuestion;
using Application.Forms.Commands.UpdateSection;
using Domain.ValueObjects;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

// DHSC/Departmental admin authoring surface for the activity-record form (BLUM-6444). The LA-facing read of
// the active schema lives on ActivitiesController (GET activities/form-schema). Behind BLUM_9395, with its pages.
[Route("admin/activity-form")]
[ApiController]
[Authorize(Roles = Role.Admin)]
[RequiresFeature(Feature.BLUM_9395)]
public class FormDesignerController(
    ISender sender,
    IFormDesignerControllerService controllerService,
    ICategoryQueries categoryQueries)
    : ControllerBase
{
    [HttpGet("schema")]
    public async Task<ActionResult<FormSchemaDto>> Schema(CancellationToken cancellationToken)
    {
        return await controllerService.GetFullSchemaAsync(cancellationToken);
    }

    // The activity categories a section can be scoped to (DHSC reference data).
    [HttpGet("categories")]
    public async Task<ActionResult<IReadOnlyList<TaxonomyCategoryDto>>> Categories(CancellationToken cancellationToken)
    {
        var categories = await categoryQueries.GetActiveCategoriesAsync(cancellationToken);
        return Ok(categories.Select(c => new TaxonomyCategoryDto(c.Id.Value, c.Name)).ToList());
    }

    [HttpPost("sections")]
    public async Task<ActionResult<FormSectionDto>> CreateSection(
        [FromBody] CreateSectionRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new CreateSectionCommand { Title = request.Title, Description = request.Description, CategoryId = request.CategoryId },
            cancellationToken);
        return result.Match<ActionResult<FormSectionDto>>(
            section => controllerService.ToDto(section),
            error => error.ToObjectResult());
    }

    [HttpPut("sections/{sectionId:guid}")]
    public async Task<ActionResult<FormSectionDto>> UpdateSection(
        [FromRoute] Guid sectionId,
        [FromBody] UpdateSectionRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new UpdateSectionCommand { Id = sectionId, Title = request.Title, Description = request.Description, CategoryId = request.CategoryId },
            cancellationToken);
        return result.Match<ActionResult<FormSectionDto>>(
            section => controllerService.ToDto(section),
            error => error.ToObjectResult());
    }

    [HttpDelete("sections/{sectionId:guid}")]
    public async Task<ActionResult> DeleteSection(
        [FromRoute] Guid sectionId,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new DeleteSectionCommand { Id = sectionId }, cancellationToken);
        return result.Match<ActionResult>(_ => NoContent(), error => error.ToObjectResult());
    }

    [HttpPost("sections/reorder")]
    public async Task<ActionResult> ReorderSections(
        [FromBody] ReorderRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new ReorderSectionsCommand { OrderedIds = request.OrderedIds ?? [] },
            cancellationToken);
        return result.Match<ActionResult>(_ => NoContent(), error => error.ToObjectResult());
    }

    [HttpPost("questions")]
    public async Task<ActionResult<FormQuestionDto>> CreateQuestion(
        [FromBody] CreateQuestionRequestDto request,
        CancellationToken cancellationToken)
    {
        var command = new CreateQuestionCommand
        {
            SectionId = request.SectionId,
            Code = request.Code,
            Label = request.Label,
            QuestionType = request.QuestionType,
            IsRequired = request.IsRequired,
            Hint = request.Hint,
            HelpText = request.HelpText,
            HelpTextSummary = request.HelpTextSummary,
            ConditionalQuestionCode = request.ConditionalQuestionCode,
            ConditionalValue = request.ConditionalValue,
            Options = MapOptions(request.Options),
        };

        var result = await sender.Send(command, cancellationToken);
        return result.Match<ActionResult<FormQuestionDto>>(
            question => controllerService.ToDto(question),
            error => error.ToObjectResult());
    }

    [HttpPut("questions/{questionId:guid}")]
    public async Task<ActionResult<FormQuestionDto>> UpdateQuestion(
        [FromRoute] Guid questionId,
        [FromBody] UpdateQuestionRequestDto request,
        CancellationToken cancellationToken)
    {
        var command = new UpdateQuestionCommand
        {
            Id = questionId,
            SectionId = request.SectionId,
            Label = request.Label,
            QuestionType = request.QuestionType,
            IsRequired = request.IsRequired,
            IsActive = request.IsActive,
            Hint = request.Hint,
            HelpText = request.HelpText,
            HelpTextSummary = request.HelpTextSummary,
            ConditionalQuestionCode = request.ConditionalQuestionCode,
            ConditionalValue = request.ConditionalValue,
            Options = MapOptions(request.Options),
        };

        var result = await sender.Send(command, cancellationToken);
        return result.Match<ActionResult<FormQuestionDto>>(
            question => controllerService.ToDto(question),
            error => error.ToObjectResult());
    }

    [HttpDelete("questions/{questionId:guid}")]
    public async Task<ActionResult> DeleteQuestion(
        [FromRoute] Guid questionId,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new DeleteQuestionCommand { Id = questionId }, cancellationToken);
        return result.Match<ActionResult>(_ => NoContent(), error => error.ToObjectResult());
    }

    [HttpPost("questions/reorder")]
    public async Task<ActionResult> ReorderQuestions(
        [FromBody] ReorderQuestionsRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new ReorderQuestionsCommand { SectionId = request.SectionId, OrderedIds = request.OrderedIds ?? [] },
            cancellationToken);
        return result.Match<ActionResult>(_ => NoContent(), error => error.ToObjectResult());
    }

    private static IReadOnlyList<QuestionOptionInput> MapOptions(IReadOnlyList<QuestionOptionRequestDto>? options) =>
        options?.Select(o => new QuestionOptionInput(o.Value, o.Label, o.DisplayOrder)).ToList() ?? [];
}