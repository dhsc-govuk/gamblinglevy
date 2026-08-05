import React from 'react';
import { AttachmentThumbnailIcon } from '../../../Icons';
import { composeTestId } from '../../../../helpers/testIds';
import './styles.css';

interface FileDownloadCardProps {
  // The download link's display text, e.g. "Quarterly report for Northumberland, 1 July 2026 to 30 September 2026".
  title: string;
  // File metadata shown under the link, e.g. "ZIP".
  metadata: string;
  onDownload: () => void;
  isLoading?: boolean;
  errorMessage?: string | null;
  // Optional stable ids for callers migrating an existing standalone download control into this card.
  downloadTestId?: string;
  errorTestId?: string;
  // Optional GOV.UK warning (e.g. the "may contain incomplete data" notice while a window is still open).
  warning?: React.ReactNode;
  testId: string;
}

// The GOV.UK attachment pattern per the submission-detail design (Figma 420-80777): a document-page thumbnail on a
// light-grey card beside a large (24px) download link with a secondary metadata line. The download is triggered via
// JavaScript (the request needs the auth token), so the "link" is a button styled as one rather than a plain <a>.
const FileDownloadCard = ({ title, metadata, onDownload, isLoading, errorMessage, downloadTestId, errorTestId, warning, testId }: FileDownloadCardProps): React.ReactElement => (
  <div className="report-file-attachment" data-testid={testId}>
    <span className="report-file-attachment__thumbnail" aria-hidden="true">
      <AttachmentThumbnailIcon />
    </span>
    <div className="report-file-attachment__body">
      {warning}
      <button
        type="button"
        className="govuk-link govuk-!-font-size-24 report-file-attachment__link"
        onClick={onDownload}
        disabled={isLoading}
        data-testid={downloadTestId ?? composeTestId(testId, 'download')}>
        {title}
      </button>
      <p className="govuk-hint report-file-attachment__metadata">{metadata}</p>
      {errorMessage && (
        <p className="govuk-error-message" data-testid={errorTestId ?? composeTestId(testId, 'error')}>
          <span className="govuk-visually-hidden">Error:</span> {errorMessage}
        </p>
      )}
    </div>
  </div>
);

export default FileDownloadCard;
