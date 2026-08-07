// Maps a quarterly-report API error to a page-level message plus per-question field errors (keyed by
// question code). Mirrors views/Activities/messages.ts and the backend SubmissionErrorFactory envelopes.

export interface ReportError {
  message: string;
  fieldErrors: Record<string, string>;
}

interface ReportErrorEnvelope {
  error?: string;
  message?: string;
  answerErrors?: { questionCode: string; message: string }[];
}

export const mapReportError = (error: unknown): ReportError => {
  const response = (error as { response?: { status?: number; data?: unknown } } | undefined)?.response;
  const status = response?.status;
  const data = response?.data as ReportErrorEnvelope | string | undefined;
  const envelope = typeof data === 'object' ? data : undefined;
  const code = envelope?.error;

  if (code === 'SUBMISSION_ANSWERS_INVALID') {
    const fieldErrors = (envelope?.answerErrors ?? []).reduce<Record<string, string>>((acc, item) => {
      acc[item.questionCode] = item.message;
      return acc;
    }, {});
    return { message: envelope?.message ?? 'Check your answers and try again.', fieldErrors };
  }
  if (code === 'REPORT_NOT_OPEN') {
    return { message: envelope?.message ?? 'This report is no longer open. You can no longer make changes.', fieldErrors: {} };
  }
  if (code === 'ACTIVITIES_NOT_CONFIRMED') {
    return { message: envelope?.message ?? 'Confirm your list of activities before entering key results.', fieldErrors: {} };
  }
  if (code === 'REPORT_INCOMPLETE') {
    return { message: envelope?.message ?? 'Complete every section before you submit this report.', fieldErrors: {} };
  }
  if (status === 409) {
    return { message: 'A report has already been created for this collection. Refresh the page and try again.', fieldErrors: {} };
  }
  if (status === 403) {
    return { message: 'You do not have permission to do this.', fieldErrors: {} };
  }
  if (status === 422 && envelope?.message) {
    return { message: envelope.message, fieldErrors: {} };
  }
  if (typeof data === 'string' && data.length > 0) {
    return { message: data, fieldErrors: {} };
  }
  return { message: 'Something went wrong. Try again.', fieldErrors: {} };
};

// The report ZIP download's failure copy lives in helpers/downloadErrors.downloadErrorMessage (BLUM-6098,
// BLUM-7239), imported directly by the screens that render it — the Submitted confirmation and SubmissionDetail,
// which share it with the DHSC screens.
