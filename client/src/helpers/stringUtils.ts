export function capitaliseFirst(str: string): string {
  if (!str) return '';
  return str.charAt(0).toUpperCase() + str.slice(1);
}

export function booleanToYesNo(value: boolean): string {
  return value ? 'Yes' : 'No';
}

export function stringFromArray(arr: string[], separator = ' '): string {
  return arr.filter(Boolean).join(separator);
}
