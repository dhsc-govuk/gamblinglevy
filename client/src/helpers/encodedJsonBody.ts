import { AxiosRequestConfig } from 'axios';

// Matched by AcceptsEncodedJsonBodyAttribute in api/src/Api/Common — renaming either side makes every encoded save a
// model-binding 400.
export const ENCODED_BODY_HEADER = 'X-Body-Encoding';

// UTF-8 first: btoa only takes Latin-1, and answers carry curly quotes, en dashes and the like. Chunked so a long
// body cannot overflow the argument limit of String.fromCharCode.
const toBase64 = (text: string): string => {
  const bytes = new TextEncoder().encode(text);
  let binary = '';
  for (let offset = 0; offset < bytes.length; offset += 0x8000) {
    binary += String.fromCharCode(...bytes.subarray(offset, offset + 0x8000));
  }
  return btoa(binary);
};

// The body and config for a write whose JSON carries what LAs type (BLUM-8917). DAC's gateway WAF answers 403 to
// ordinary answer text and to escaped JSON such as the funded-roles repeater's value, which users saw as "You do not
// have permission to do this". Base64 gives it nothing to match; the API's EncodedJsonBodyMiddleware decodes it
// before model binding, so validation and error envelopes are unchanged. Only for actions marked
// [AcceptsEncodedJsonBody] — anywhere else the envelope fails model binding.
//
// Use as `axios.put<T>(url, ...encodedJsonBody(body))`.
export const encodedJsonBody = (body: unknown): [unknown, AxiosRequestConfig] => [{ payload: toBase64(JSON.stringify(body)) }, { headers: { [ENCODED_BODY_HEADER]: 'base64' } }];
