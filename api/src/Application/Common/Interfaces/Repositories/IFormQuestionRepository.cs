using Domain.Forms;
using LanguageExt;

namespace Application.Common.Interfaces.Repositories;

public interface IFormQuestionRepository
{
    Task<FormQuestion> AddAsync(FormQuestion entity, CancellationToken cancellationToken = default);

    // Loads a question with its options, tracked, for mutation. The update flow mutates the tracked options
    // collection and saves via SaveChangesAsync.
    Task<Option<FormQuestion>> GetByIdAsync(FormQuestionId id, CancellationToken cancellationToken = default);

    // Resolves a question by its unique code (no tracking) — used to enforce code uniqueness and conditional refs.
    Task<Option<FormQuestion>> FindByCodeAsync(string code, CancellationToken cancellationToken = default);

    // All questions with options, ordered (no tracking) — feeds schema/list reads and answer validation.
    Task<IReadOnlyList<FormQuestion>> GetAllAsync(CancellationToken cancellationToken = default);

    // Total questions (active or not) in a section — guards section deletion.
    Task<int> CountBySectionAsync(FormSectionId sectionId, CancellationToken cancellationToken = default);

    // True when another question (excluding the given id) reveals itself based on the given parent code —
    // guards deletion of a question referenced by a conditional rule.
    Task<bool> AnyReferencingConditionalAsync(string parentCode, FormQuestionId excludingId, CancellationToken cancellationToken = default);

    Task<int> GetMaxDisplayOrderForSectionAsync(FormSectionId sectionId, CancellationToken cancellationToken = default);

    // Sets display order within a section from the supplied ordered id list (index-based) in a single save.
    Task ReorderAsync(FormSectionId sectionId, IReadOnlyList<Guid> orderedIds, CancellationToken cancellationToken = default);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    Task<int> DeleteAsync(FormQuestionId id, CancellationToken cancellationToken = default);
}