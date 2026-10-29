import { describe, expect, it } from 'vitest';
import { fileMetadata, formatFileSize } from './fileSize';

describe('formatFileSize', () => {
  it.each([
    [0, '0 bytes'],
    [1, '1 byte'],
    [512, '512 bytes'],
    [999, '999 bytes'],
    // Four digits never show: the unit steps up instead.
    [1000, '0.977 KB'],
    [1024, '1 KB'],
    [1536, '1.5 KB'],
    [13824, '13.5 KB'],
    // The design's own example.
    [398336, '389 KB'],
    [1024000, '0.977 MB'],
    // Just under the next unit rounds up into it, not to "1020 KB", and never into exponent notation.
    [1048575, '1 MB'],
    [1048576, '1 MB'],
    [1258291, '1.2 MB'],
    [5 * 1024 * 1024 * 1024, '5 GB'],
  ])('formats %d bytes as %s', (bytes, expected) => {
    expect(formatFileSize(bytes)).toBe(expected);
  });
});

describe('fileMetadata', () => {
  it('captions the format and the size together', () => {
    expect(fileMetadata('ZIP', 398336)).toBe('ZIP, 389 KB');
  });

  it.each([null, undefined])('falls back to the format alone when the size is %s', (size) => {
    expect(fileMetadata('ZIP', size)).toBe('ZIP');
  });

  it('keeps a zero-byte size rather than treating it as unknown', () => {
    expect(fileMetadata('CSV', 0)).toBe('CSV, 0 bytes');
  });
});
