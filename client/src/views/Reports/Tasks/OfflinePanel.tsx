import React, { useState } from 'react';
import { GovUKButton, GovUKFieldset, GovUKTable, GovUKWarningText, Paragraph, type Column } from '../../../components/GovUKComponents';
import FileDownloadCard from '../../../components/Global/Reports/FileDownloadCard';
import { downloadErrorMessage } from '../../../helpers/downloadErrors';
import { saveBlob } from '../../../helpers/downloadFile';
import { httpStatusOf } from '../../../helpers/httpErrors';
import { composeTestId, slugifyTestId } from '../../../helpers/testIds';
import { isOfflineFeatureOff, mapOfflineUploadError } from '../messages';
import {
  downloadOfflineTemplate,
  OFFLINE_TEMPLATE_SLUG,
  type OfflineAnnotatedFileDto,
  type OfflineUploadErrorDto,
  type ReportModuleCode,
  type SubmissionSectionDto,
} from '../queries';
import type { OfflineUploadState } from './useOfflineUpload';

export const OFFLINE_FILE_INPUT_ID = 'offline-upload-file';

interface OfflinePanelProps {
  collectionId: string;
  sections: SubmissionSectionDto[];
  upload: OfflineUploadState;
}

interface IndexedUploadError extends OfflineUploadErrorDto {
  index: number;
}

const errorColumns: Column<IndexedUploadError>[] = [
  { key: 'row', header: 'Row' },
  { key: 'column', header: 'Column', render: (error) => error.column ?? '' },
  { key: 'question', header: 'Question', render: (error) => error.question ?? '' },
  { key: 'message', header: 'Error' },
];

// The annotated file already came back in the 422 body, so saving it needs no request.
const saveAnnotatedFile = (file: OfflineAnnotatedFileDto) => {
  const bytes = Uint8Array.from(window.atob(file.contentBase64), (character) => character.charCodeAt(0));
  saveBlob(new Blob([bytes], { type: 'text/csv' }), file.fileName);
};

// A refused download in the panel's own words: the report's write gate reads as it does for the upload, anything
// else (a 429 included) as it does for every other report download.
const templateErrorMessage = (error: unknown): string => {
  const status = httpStatusOf(error);
  if (status === 409 || status === 422) {
    const problem = mapOfflineUploadError(error);
    if (problem.kind === 'file') {
      return problem.message;
    }
  }

  return downloadErrorMessage(error);
};

// "Complete your report offline" (BLUM-8310): a spreadsheet per section to download, and one upload that takes any
// of them back. The page around it owns the error summary and the success banner, so both sit at the top of the page
// with its other messages.
const OfflinePanel = ({ collectionId, sections, upload }: OfflinePanelProps): React.ReactElement => {
  const [downloading, setDownloading] = useState<ReportModuleCode | null>(null);
  const [downloadErrors, setDownloadErrors] = useState<Partial<Record<ReportModuleCode, string>>>({});

  const download = async (moduleCode: ReportModuleCode) => {
    setDownloading(moduleCode);
    setDownloadErrors((errors) => ({ ...errors, [moduleCode]: undefined }));
    try {
      await downloadOfflineTemplate(collectionId, moduleCode);
    } catch (error) {
      if (isOfflineFeatureOff(error)) {
        upload.markUnavailable();
        return;
      }

      setDownloadErrors((errors) => ({ ...errors, [moduleCode]: templateErrorMessage(error) }));
    } finally {
      setDownloading(null);
    }
  };

  // Only the sections that have a template: DHSC can add its own form modules to a collection, and each becomes a
  // task-list section this feature knows nothing about.
  const offlineSections = sections.filter((section) => Object.prototype.hasOwnProperty.call(OFFLINE_TEMPLATE_SLUG, section.moduleCode));
  const invalid = upload.invalid;
  const listedErrors: IndexedUploadError[] = (invalid?.errors ?? []).map((error, index) => ({ ...error, index }));

  return (
    <section className="reports-tasks__section reports-tasks__offline" data-testid="reports-tasks-offline">
      <h2 className="govuk-heading-m">Complete your report offline</h2>
      <div className="reports-tasks__offline-intro">
        <Paragraph>You can download a spreadsheet for each section, fill it in and upload it here instead of answering the questions on screen.</Paragraph>
        <Paragraph>
          Each spreadsheet includes the answers you have already saved. When you upload a spreadsheet with no errors, we save its answers and mark that section as completed. You
          still need to submit your report.
        </Paragraph>
        <GovUKWarningText data-testid="reports-tasks-offline-warning">Uploading a spreadsheet replaces all the answers you have saved for that section.</GovUKWarningText>
      </div>

      <h3 className="govuk-heading-s">Download a spreadsheet</h3>
      <div className="reports-tasks__offline-downloads">
        {offlineSections.map((section) => {
          const testId = composeTestId('reports-tasks-offline-template', slugifyTestId(section.moduleCode)) ?? 'reports-tasks-offline-template';
          return (
            <FileDownloadCard
              key={section.moduleCode}
              title={`‘${section.moduleName}’ spreadsheet`}
              metadata="CSV"
              onDownload={() => download(section.moduleCode)}
              isLoading={downloading === section.moduleCode}
              errorMessage={downloadErrors[section.moduleCode]}
              testId={testId}
            />
          );
        })}
      </div>

      <h3 className="govuk-heading-s">Upload a spreadsheet</h3>
      <form
        className="reports-tasks__offline-upload"
        noValidate
        onSubmit={(event) => {
          event.preventDefault();
          upload.upload();
        }}>
        <GovUKFieldset.FileUpload
          key={upload.inputKey}
          id={OFFLINE_FILE_INPUT_ID}
          name="file"
          label="Choose your completed spreadsheet"
          hint="Save it as ‘CSV UTF-8 (Comma delimited)’. We will work out which section it is for."
          accept=".csv,text/csv"
          error={upload.fileError ?? undefined}
          onChange={upload.chooseFile}
          testId="reports-tasks-offline-file"
        />
        <GovUKButton type="submit" isLoading={upload.isUploading} data-testid="reports-tasks-offline-upload">
          Upload spreadsheet
        </GovUKButton>
        {/* A loading GovUKButton hides its label and is disabled, so it says nothing while a file of up to 5MB is
            checked; this says it instead (WCAG 2.2 SC 4.1.3). */}
        <p className="govuk-visually-hidden" role="status" data-testid="reports-tasks-offline-status">
          {upload.isUploading ? 'Uploading your spreadsheet' : ''}
        </p>
      </form>

      {invalid && (
        <div className="reports-tasks__offline-errors">
          <h3 className="govuk-heading-s">Errors in your file</h3>
          <Paragraph>Nothing from this file has been saved. Download your file with the errors marked, fix them in your spreadsheet and upload it again.</Paragraph>
          {invalid.errorCount > listedErrors.length && (
            <Paragraph data-testid="reports-tasks-offline-errors-count">
              {`Showing the first ${listedErrors.length} of ${invalid.errorCount} errors. Your file with errors marked lists them all.`}
            </Paragraph>
          )}
          <GovUKTable
            label="Errors in your file"
            data={listedErrors}
            columns={errorColumns}
            keyExtractor={(error) => String(error.index)}
            searchable={false}
            testId="reports-tasks-offline-errors"
          />
          <GovUKButton
            type="button"
            className="govuk-button govuk-button--secondary"
            onClick={() => saveAnnotatedFile(invalid.annotatedFile)}
            data-testid="reports-tasks-offline-download-annotated">
            Download your file with errors marked
          </GovUKButton>
        </div>
      )}
    </section>
  );
};

export default OfflinePanel;
