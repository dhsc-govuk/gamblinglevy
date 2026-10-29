import { useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import {
  laFundingDashboardKey,
  reportActivitiesKey,
  reportActivityFormsKey,
  reportContextKey,
  reportFundingSpendKey,
  reportStrategicFeedbackKey,
  reportTaskListKey,
} from '../../../helpers/queriesParams';
import { httpStatusOf } from '../../../helpers/httpErrors';
import { uploadOfflineReport } from '../mutations';
import { mapOfflineUploadError, OFFLINE_FILE_MESSAGES, OFFLINE_MAX_FILE_BYTES } from '../messages';
import type { OfflineUploadInvalidDto, ReportModuleCode } from '../queries';

export interface OfflineUploadState {
  // The file input's single message: a problem with the file itself, or the count of the errors in it.
  fileError: string | null;
  // The file was read and its answers have problems; nothing was saved.
  invalid: OfflineUploadInvalidDto | null;
  // The section the last successful upload saved and completed.
  uploadedModule: ReportModuleCode | null;
  // Bumped on every refused upload / every success, to move focus to the error summary / success banner again.
  problemAttempts: number;
  successCount: number;
  // The routes answered 404: the flag was switched off after this page loaded.
  unavailable: boolean;
  // Remounts the uncontrolled file input, which is the only way to clear it.
  inputKey: number;
  isUploading: boolean;
  chooseFile: (file: File | null) => void;
  upload: () => void;
  markUnavailable: () => void;
}

const errorCountMessage = (count: number) =>
  `The selected file has ${count} ${count === 1 ? 'error' : 'errors'}. Fix ${count === 1 ? 'it' : 'them'} in your spreadsheet and upload it again`;

// "Complete your report offline" (BLUM-8310): the upload's state for the Tasks page, which renders its error summary
// and success banner at the top of the page and the controls in OfflinePanel.
const useOfflineUpload = (collectionId: string): OfflineUploadState => {
  const queryClient = useQueryClient();
  const [file, setFile] = useState<File | null>(null);
  const [fileError, setFileError] = useState<string | null>(null);
  const [invalid, setInvalid] = useState<OfflineUploadInvalidDto | null>(null);
  const [uploadedModule, setUploadedModule] = useState<ReportModuleCode | null>(null);
  const [problemAttempts, setProblemAttempts] = useState(0);
  const [successCount, setSuccessCount] = useState(0);
  const [unavailable, setUnavailable] = useState(false);
  const [inputKey, setInputKey] = useState(0);

  // The input is emptied after every refusal, as a server-rendered GDS form would be. The next step is to fix the
  // spreadsheet and upload it again; a File the browser picked before the person saved their fix no longer matches
  // what is on disk, and Chrome then fails the request outright (ERR_UPLOAD_FILE_CHANGED).
  const refuse = (message: string, rows: OfflineUploadInvalidDto | null = null) => {
    setFile(null);
    setInputKey((key) => key + 1);
    setFileError(message);
    setInvalid(rows);
    setUploadedModule(null);
    setProblemAttempts((attempts) => attempts + 1);
  };

  // The flag was switched off since the page loaded: the panel goes, and with it anything it was showing.
  const markUnavailable = () => {
    setFileError(null);
    setInvalid(null);
    setUnavailable(true);
  };

  const mutation = useMutation({
    mutationFn: uploadOfflineReport,
    onSuccess: (response) => {
      // Seed the task list from the response rather than invalidating it: react-query keeps serving the stale
      // copy while a refetch is in flight, and the section guards read it (see the report-guard race in memory).
      queryClient.setQueryData(reportTaskListKey(collectionId), { ...response, data: response.data.taskList });
      queryClient.invalidateQueries({ queryKey: reportContextKey(collectionId) });
      switch (response.data.moduleCode) {
        case 'activity_key_results':
          queryClient.invalidateQueries({ queryKey: reportActivitiesKey(collectionId) });
          queryClient.invalidateQueries({ queryKey: reportActivityFormsKey(collectionId) });
          break;
        case 'funding_spent':
          queryClient.invalidateQueries({ queryKey: reportFundingSpendKey(collectionId) });
          // Actual spend feeds the funding dashboard tile (BLUM-7701).
          queryClient.invalidateQueries({ queryKey: laFundingDashboardKey });
          break;
        case 'strategic_feedback':
          queryClient.invalidateQueries({ queryKey: reportStrategicFeedbackKey(collectionId) });
          break;
      }

      setFile(null);
      setFileError(null);
      setInvalid(null);
      setUploadedModule(response.data.moduleCode);
      setSuccessCount((count) => count + 1);
      setInputKey((key) => key + 1);
    },
    onError: (error) => {
      const problem = mapOfflineUploadError(error);
      if (problem.kind === 'unavailable') {
        markUnavailable();
        return;
      }

      // Submitted elsewhere since the page loaded: refetching the task list lets the page's own redirect to the
      // submitted confirmation take over.
      if (httpStatusOf(error) === 409) {
        queryClient.invalidateQueries({ queryKey: reportTaskListKey(collectionId) });
      }

      if (problem.kind === 'rows') {
        refuse(errorCountMessage(problem.invalid.errorCount), problem.invalid);
        return;
      }

      refuse(problem.message);
    },
  });

  const chooseFile = (chosen: File | null) => {
    setFile(chosen);
  };

  const upload = () => {
    if (!file) {
      refuse(OFFLINE_FILE_MESSAGES.missing);
      return;
    }

    const name = file.name.toLowerCase();
    if (!name.endsWith('.csv')) {
      refuse(name.endsWith('.xlsx') || name.endsWith('.xls') ? OFFLINE_FILE_MESSAGES.spreadsheet : OFFLINE_FILE_MESSAGES.notCsv);
      return;
    }

    if (file.size > OFFLINE_MAX_FILE_BYTES) {
      refuse(OFFLINE_FILE_MESSAGES.tooLarge);
      return;
    }

    mutation.mutate({ collectionId, file });
  };

  return {
    fileError,
    invalid,
    uploadedModule,
    problemAttempts,
    successCount,
    unavailable,
    inputKey,
    isUploading: mutation.isPending,
    chooseFile,
    upload,
    markUnavailable,
  };
};

export default useOfflineUpload;
