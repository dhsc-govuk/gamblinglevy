using Api.Dtos;
using Domain.Forms;

namespace Api.Services.Abstract;

public interface IFormDesignerControllerService
{
    FormSectionDto ToDto(FormSection section);
    FormQuestionDto ToDto(FormQuestion question);

    // Full schema (including inactive sections/questions) — for the DHSC Form Designer authoring view.
    Task<FormSchemaDto> GetFullSchemaAsync(CancellationToken cancellationToken);

    // Active schema only — for the LA-facing activity-creation render.
    Task<FormSchemaDto> GetActiveSchemaAsync(CancellationToken cancellationToken);

    // Active schema scoped to one activity category (plus global sections) — used by the create/edit flow.
    Task<FormSchemaDto> GetActiveSchemaForCategoryAsync(Guid? categoryId, CancellationToken cancellationToken);
}