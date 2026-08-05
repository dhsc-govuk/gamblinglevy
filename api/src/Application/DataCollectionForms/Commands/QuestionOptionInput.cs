namespace Application.DataCollectionForms.Commands;

// A selectable option supplied when creating or updating a choice-type forms-builder question.
public record QuestionOptionInput(string Value, string Label);