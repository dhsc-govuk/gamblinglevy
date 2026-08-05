// Reads the structured error code returned by the forms-builder error factory.
export const formsErrorCode = (error: unknown): string | undefined => (error as { response?: { data?: { error?: string } } } | undefined)?.response?.data?.error;

// Maps a forms-builder API error to a single user-facing message.
export const mapFormsError = (error: unknown): string => {
  const response = (error as { response?: { data?: { error?: string; message?: string } } } | undefined)?.response;
  const code = response?.data?.error;

  switch (code) {
    case 'DUPLICATE_CODE':
      return 'A form with this code already exists. Choose a different code.';
    case 'DUPLICATE_QUESTION_CODE':
      return 'A question with this code already exists in this form. Choose a different code.';
    case 'MODULE_PUBLISHED':
      return 'This form has been published and can no longer be changed. Create a new draft to make changes.';
    case 'MODULE_NOT_DRAFT':
      return 'Only draft forms can be edited or published.';
    case 'SECTION_NOT_EMPTY':
      return 'Move or delete this section’s questions before deleting the section.';
    case 'OPTIONS_REQUIRED':
      return 'Add at least one option for this question type.';
    case 'MODULE_IN_USE':
      return 'This form is in use by a data collection and cannot be deleted.';
    default:
      return response?.data?.message || 'Something went wrong. Try again.';
  }
};
