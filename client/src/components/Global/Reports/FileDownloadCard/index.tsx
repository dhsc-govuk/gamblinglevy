import React, { useId } from 'react';
import { AttachmentThumbnailIcon } from '../../../Icons';
import { composeTestId } from '../../../../helpers/testIds';
import './styles.css';

interface FileDownloadCardProps {
  // The download link's display text, e.g. "Quarterly report for Northumberland, 1 July 2026 to 30 September 2026".
  title: string;
  // File metadata shown under the link: format and size, e.g. "ZIP, 389 KB" (helpers/fileSize's fileMetadata).
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
const FileDownloadCard = ({ title, metadata, onDownload, isLoading, errorMessage, downloadTestId, errorTestId, warning, testId }: FileDownloadCardProps): React.ReactElement => {
  // The button's accessible name is the title alone, so the caption is described onto it: it is the only thing telling
  // a screen-reader user this control downloads a file, and how big (the Admin detail screen's precedent). The DOM id
  // is React's own rather than the testid, so renaming a QA hook can never unwire the description.
  const metadataId = useId();

  return (
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
          aria-describedby={metadataId}
          data-testid={downloadTestId ?? composeTestId(testId, 'download')}>
          {title}
        </button>
        <p className="govuk-hint report-file-attachment__metadata" id={metadataId} data-testid={composeTestId(testId, 'metadata')}>
          {metadata}
        </p>
        {errorMessage && (
          // role="alert" so a failed download is announced: focus stays on the download button, so without a live
          // region a screen-reader user waits for a file that never arrives (WCAG 2.2 SC 4.1.3). The Admin screens
          // already did this inline; this is the shared card both LA surfaces render.
          <p className="govuk-error-message" role="alert" data-testid={errorTestId ?? composeTestId(testId, 'error')}>
            <span className="govuk-visually-hidden">Error:</span> {errorMessage}
          </p>
        )}
      </div>
    </div>
  );
};

export default FileDownloadCard;
