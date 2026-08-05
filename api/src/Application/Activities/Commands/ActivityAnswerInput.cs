namespace Application.Activities.Commands;

// A single dynamic-question answer submitted alongside an activity (BLUM-6444). Keyed by the question code;
// for checkbox questions Value holds the selected values comma-joined.
public record ActivityAnswerInput(string QuestionCode, string? Value);