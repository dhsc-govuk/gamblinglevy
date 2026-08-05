namespace Application.Forms.Commands;

// A selectable option supplied when creating or updating a choice-type question.
public record QuestionOptionInput(string Value, string Label, int DisplayOrder);