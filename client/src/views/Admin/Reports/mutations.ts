import { downloadFile } from '../../../helpers/downloadFile';

// Downloads the window-level submissions export (ZIP of per-section CSVs) for a reporting window (BLUM-6100): a summary
// file (one row per assigned org — name, status, submitted date, total reported spend, financial year, quarter) plus
// the full question-and-answer data from all three report sections, aggregated across every org. The filename comes
// from the response (submissions-{financialYear}-q{quarter}.zip).
//
// An optional region narrows it to the organisations in that region (BLUM-7682), and the server names the file after it
// (…-q1-north-east.zip). Omit the parameter entirely for every organisation — the API treats an empty ?region= as "all"
// too, but not sending it keeps the URL, and therefore the request, identical to what it was before the filter existed.
export const downloadWindowSubmissionsExport = (collectionId: string, region?: string): Promise<void> =>
  downloadFile(
    `data-collections/${collectionId}/submissions/export${region ? `?region=${encodeURIComponent(region)}` : ''}`,
    // Only used when Content-Disposition is missing — a proxy that strips it. Name the region anyway, or the degraded
    // path saves a regional export under an all-regions-looking name.
    region ? `submissions-${region.toLowerCase().replace(/ /g, '-')}.zip` : 'submissions.zip'
  );

// Downloads a single organisation's submitted quarterly report (ZIP of CSVs) for DHSC analysis.
export const downloadOrgReport = (collectionId: string, organisationId: string): Promise<void> =>
  downloadFile(`data-collections/${collectionId}/submissions/${organisationId}/export`, 'quarterly-report.zip');
