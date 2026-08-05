// Reads the structured error code returned by FormDesignerErrorFactory.
export const formDesignerErrorCode = (error: unknown): string | undefined =>
  (error as { response?: { data?: { error?: string } } } | undefined)?.response?.data?.error;

// Maps a Form Designer API error to a single user-facing message.
export const mapFormDesignerError = (error: unknown): string => {
  const response = (error as { response?: { data?: { error?: string; message?: string } } } | undefined)?.response;
  const code = response?.data?.error;

  switch (code) {
    case 'DUPLICATE_CODE':
      return 'A question with this code already exists. Choose a different code.';
    case 'QUESTION_REFERENCED':
      return 'This question is used by another question’s conditional rule. Remove that rule first, or deactivate this question instead.';
    case 'SECTION_NOT_EMPTY':
      return 'Move or delete this section’s questions before deleting the section.';
    case 'OPTIONS_REQUIRED':
      return 'Add at least one option for this question type.';
    case 'INVALID_CONDITIONAL':
      return 'The selected parent question for the conditional rule is not valid.';
    default:
      return response?.data?.message || 'Something went wrong. Try again.';
  }
};
