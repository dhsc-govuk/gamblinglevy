import axios from 'axios';

// Fetches an authenticated file response as a blob and triggers a browser save. The filename comes from the
// response Content-Disposition header, falling back to the given name. Used by the report/export download buttons:
// the MSAL token and mfa_session cookie are attached by the axios interceptors, so a plain anchor href would not
// carry them — the file must be fetched via axios and saved from the resulting blob.
export const downloadFile = async (url: string, fallbackName: string): Promise<void> => {
  const response = await axios.get<Blob>(url, { responseType: 'blob' });

  const disposition = response.headers['content-disposition'] as string | undefined;
  const match = disposition?.match(/filename\*?=(?:UTF-8'')?"?([^";]+)"?/i);
  const fileName = match ? decodeURIComponent(match[1]) : fallbackName;

  const objectUrl = window.URL.createObjectURL(response.data);
  const link = document.createElement('a');
  link.href = objectUrl;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  window.URL.revokeObjectURL(objectUrl);
};
