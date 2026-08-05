// Maps a data-collection API error to a single user-facing message for the error summary / notification banner.
export const mapCollectionError = (error: unknown): string => {
  const response = (error as { response?: { status?: number; data?: unknown } } | undefined)?.response;
  const status = response?.status;
  const data = response?.data as { error?: string; message?: string; errors?: { property?: string; message?: string }[] } | string | undefined;

  // FluentValidation envelope from ValidationExceptionMiddleware — surface the first rule message verbatim.
  if (status === 400 && typeof data === 'object' && data?.error === 'validation_failed' && data.errors?.[0]?.message) {
    return data.errors[0].message;
  }
  if (status === 409) {
    return 'A data collection with this name already exists. Choose a different name.';
  }
  if (status === 404) {
    return 'This data collection was not found. It may have been removed.';
  }
  if (status === 403) {
    return 'You do not have permission to do this.';
  }
  if (status === 422 && typeof data === 'object' && data?.message) {
    return data.message;
  }
  if (typeof data === 'string' && data.length > 0) {
    return data;
  }
  return 'Something went wrong. Try again.';
};
