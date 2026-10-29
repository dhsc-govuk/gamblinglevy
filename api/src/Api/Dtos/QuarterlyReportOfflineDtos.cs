namespace Api.Dtos;

// "Complete your report offline" (BLUM-8310). A template download is a file, not JSON; this is what the controller
// service hands the controller to serve.
public record OfflineTemplateFileDto(byte[] Content, string FileName);

// A successful upload: which section it saved and completed, and the task list as it now stands, so the client
// can seed its cache from the response instead of refetching (see the report guards' invalidate/navigate race).
public record OfflineUploadResultDto(string ModuleCode, SubmissionTaskListDto TaskList);