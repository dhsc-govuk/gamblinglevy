import { downloadFile } from '../../../helpers/downloadFile';

// Downloads the window-level submissions export (ZIP of per-section CSVs) for a reporting window (BLUM-6100): a summary
// file (one row per assigned org — name, status, submitted date, total reported spend, financial year, quarter) plus
// the full question-and-answer data from all three report sections, aggregated across every org. The filename comes
// from the response (submissions-{financialYear}-q{quarter}.zip).
export const downloadWindowSubmissionsExport = (collectionId: string): Promise<void> => downloadFile(`data-collections/${collectionId}/submissions/export`, 'submissions.zip');

// Downloads a single organisation's submitted quarterly report (ZIP of CSVs) for DHSC analysis.
export const downloadOrgReport = (collectionId: string, organisationId: string): Promise<void> =>
  downloadFile(`data-collections/${collectionId}/submissions/${organisationId}/export`, 'quarterly-report.zip');
