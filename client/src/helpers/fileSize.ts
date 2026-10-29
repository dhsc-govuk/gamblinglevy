// A downloadable file's caption in the GOV.UK attachment pattern — format and size together ("ZIP, 389 KB") so a user
// on a slow or metered connection can decide before committing (BLUM-9018).

const UNITS = ['bytes', 'KB', 'MB', 'GB'];

// Binary units (1 KB = 1024 bytes) and three significant figures with trailing zeros dropped — "389 KB", "13.5 KB",
// "1.2 MB" — the way GOV.UK captions its own attachments. The unit steps up whenever the rounded figure would need four
// digits, so a file just under 1 MB reads "1 MB" (or "0.977 MB"), never "1020 KB". Going through Number() is what
// keeps toPrecision from answering in exponent form ("1.02e+3").
export const formatFileSize = (bytes: number): string => {
  let value = bytes;
  let unit = 0;
  while (Number(value.toPrecision(3)) >= 1000 && unit < UNITS.length - 1) {
    value /= 1024;
    unit += 1;
  }

  if (unit === 0) return value === 1 ? '1 byte' : `${value} bytes`;
  return `${Number(value.toPrecision(3))} ${UNITS[unit]}`;
};

// The format alone when the size is unknown: the server leaves it null until the file exists, and when measuring it
// failed — a caption without a size is still true, where a guessed one would not be.
export const fileMetadata = (format: string, sizeBytes: number | null | undefined): string => (sizeBytes == null ? format : `${format}, ${formatFileSize(sizeBytes)}`);
