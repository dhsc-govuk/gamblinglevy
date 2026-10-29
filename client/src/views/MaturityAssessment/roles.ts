import { composeTestId } from '../../helpers/testIds';
import type { FormQuestionDto } from '../../types/formSchema';
import type { MaturityQuestionDto } from './queries';

// The funded-roles repeater (BLUM-7855, Figma 2744-182477): "Which roles are being funded by the levy?" is one
// question whose answer is a JSON array of {role, time, otherRole} rows — the shape the backend's
// MaturityRolesAnswer parses and re-serialises canonically. Every helper here is pure so the row ids the error
// summary links to, the rows the check page lists and the value that is saved come from one place.

export interface RoleEntry {
  role: string;
  time: string;
  otherRole: string;
}

export const MAX_ROLES = 20;

export const OTHER_ROLE = 'other';

export const MAX_OTHER_ROLE_LENGTH = 255;

// The repeater's two per-row legends. Check-your-answers prefixes each with "Role N: " (Figma 2744-182109), so the
// form and its summary cannot word the same question differently.
export const ROLE_LEGEND = 'What’s the role?';
export const ROLE_TIME_LEGEND = 'How much of the staff member’s time is the funding paying for?';

export const emptyRole = (): RoleEntry => ({ role: '', time: '', otherRole: '' });

export const parseRoles = (json: string | undefined | null): RoleEntry[] => {
  if (!json || !json.trim()) {
    return [];
  }
  try {
    const parsed: unknown = JSON.parse(json);
    if (!Array.isArray(parsed)) {
      return [];
    }
    return parsed.map((entry) => {
      const row = (entry ?? {}) as { role?: unknown; time?: unknown; otherRole?: unknown };
      return {
        role: typeof row.role === 'string' ? row.role : '',
        time: typeof row.time === 'string' ? row.time : '',
        otherRole: typeof row.otherRole === 'string' ? row.otherRole : '',
      };
    });
  } catch {
    return [];
  }
};

const isBlank = (entry: RoleEntry): boolean => !entry.role && !entry.time && !entry.otherRole.trim();

// Rows the user never touched are dropped rather than sent as three empty strings the server would refuse.
export const cleanRoles = (entries: RoleEntry[]): RoleEntry[] => entries.filter((entry) => !isBlank(entry));

type ExclusiveQuestion = Pick<MaturityQuestionDto, 'exclusiveOptionValue'>;

// The question's exclusive option — the funded roles' "None" (BLUM-9112), which the backend catalogue declares as the
// question's exclusiveOptionValue. It answers the question on its own: offered only while there is a single row, it asks
// no time and is saved as that one row (the backend's MaturityRolesAnswer.IsExclusive / IsExclusiveAnswer).
export const isExclusiveRole = (question: ExclusiveQuestion, role: string): boolean => question.exclusiveOptionValue != null && role === question.exclusiveOptionValue;

// Judged on the rows as the repeater shows them, blank ones included — "one row" is the same rule that decides whether
// the exclusive option is offered. Saved answers hold no blank rows; rolesSummaryRows cleans them first anyway.
export const isExclusiveAnswer = (question: ExclusiveQuestion, rows: RoleEntry[]): boolean => rows.length === 1 && isExclusiveRole(question, rows[0].role);

// The first role option every row shows — where focus lands on a new row and where a role error's summary link points.
// The exclusive option is skipped because a second row does not offer it.
export const firstRoleOption = (question: Pick<MaturityQuestionDto, 'options' | 'exclusiveOptionValue'>) =>
  question.options.find((option) => !isExclusiveRole(question, option.value));

// The wire shape: otherRole is null unless the row is an "Other role"; the exclusive row carries no time.
export const serialiseRoles = (question: ExclusiveQuestion, entries: RoleEntry[]): string =>
  JSON.stringify(
    cleanRoles(entries).map((entry) => ({
      role: entry.role,
      time: isExclusiveRole(question, entry.role) ? '' : entry.time,
      otherRole: entry.role === OTHER_ROLE && entry.otherRole.trim() ? entry.otherRole.trim() : null,
    }))
  );

export type RoleField = 'role' | 'time' | 'other';

// The DOM name/id of one control in the repeater. Rows are numbered from 1 because the number is read out to the
// user ("Role 1", "for role 1") and appears in the error messages.
export const roleFieldName = (root: string, index: number, field: RoleField): string => `${root}-${index + 1}-${field}`;

// The row index and control a repeater error key (roleFieldName) names, or null for a key that is not one.
const parseRoleKey = (root: string, key: string): { index: number; field: RoleField } | null => {
  const match = key.startsWith(`${root}-`) ? key.slice(root.length + 1).match(/^(\d+)-(role|time|other)$/) : null;
  return match ? { index: Number(match[1]) - 1, field: match[2] as RoleField } : null;
};

// Whether the repeater is not showing the control an error key names: a row that does not exist, the time of the
// exclusive row, or the other-role name of a row whose role is not "Other role" (its reveal is closed).
export const isHiddenRoleControl = (question: ExclusiveQuestion, entries: RoleEntry[], root: string, key: string): boolean => {
  const parsed = parseRoleKey(root, key);
  if (!parsed) {
    return false;
  }
  // The repeater always shows one row, even with none in state.
  const row = entries[parsed.index] ?? (parsed.index === 0 ? emptyRole() : undefined);
  if (!row) {
    return true;
  }
  if (parsed.field === 'time') {
    return isExclusiveRole(question, row.role);
  }
  return parsed.field === 'other' && row.role !== OTHER_ROLE;
};

// The page's errors that still hold after the rows change, so the summary never links to nothing or to the wrong row.
// Errors only refresh on submit, so two kinds are dropped here: one on a control the repeater no longer shows
// (isHiddenRoleControl), and — after a Remove — every repeater error on the removed row or below it, because those rows
// moved up one and an error keyed by row number, whose text names "role N", would now describe a different row.
// Returns the same object when nothing is dropped.
export const pruneRoleErrors = (question: ExclusiveQuestion, root: string, errors: Record<string, string>, entries: RoleEntry[], removedIndex?: number): Record<string, string> => {
  const kept = Object.entries(errors).filter(([key]) => {
    const parsed = parseRoleKey(root, key);
    if (!parsed) {
      return true;
    }
    if (removedIndex !== undefined && parsed.index >= removedIndex) {
      return false;
    }
    return !isHiddenRoleControl(question, entries, root, key);
  });
  return kept.length === Object.keys(errors).length ? errors : Object.fromEntries(kept);
};

// Where an error-summary link for a row's control lands: a radio group links to its first option (the GDS rule the
// rest of the service follows via questionErrorTargetId); the other-role text input is its own id.
export const roleErrorTargetId = (
  root: string,
  index: number,
  field: RoleField,
  question: Pick<MaturityQuestionDto, 'options' | 'secondaryOptions' | 'exclusiveOptionValue'>
): string => {
  const name = roleFieldName(root, index, field);
  if (field === 'other') {
    return name;
  }
  const first = field === 'role' ? firstRoleOption(question) : question.secondaryOptions[0];
  return first ? `${name}-${first.value}` : name;
};

export interface ValidateRolesOptions {
  enforceRequired?: boolean;
}

// Keyed by control NAME (roleFieldName), which is what the repeater reads to show the inline message; the summary
// links are derived with roleErrorTargetId. Mirrors the backend MaturityAnswerValidator's roles rules: on Continue
// every row needs a role and a time (except the exclusive "None", which asks none), an "Other role" needs its name; a
// draft save only refuses what can never be stored (an over-long other-role name) and lets half-finished rows through.
// The server's "the exclusive row stands alone" rule is not repeated: the repeater offers it only while there is one row.
export const validateRoles = (
  root: string,
  question: Pick<MaturityQuestionDto, 'isRequired' | 'errorMessage' | 'options' | 'secondaryOptions' | 'exclusiveOptionValue'>,
  entries: RoleEntry[],
  { enforceRequired = true }: ValidateRolesOptions = {}
): Record<string, string> => {
  const errors: Record<string, string> = {};
  const rows = entries.map((entry, index) => ({ entry, index })).filter(({ entry }) => enforceRequired || !isBlank(entry));

  if (enforceRequired && question.isRequired && cleanRoles(entries).length === 0) {
    errors[roleFieldName(root, 0, 'role')] = question.errorMessage ?? 'Add at least one role funded by the levy';
    return errors;
  }

  rows.forEach(({ entry, index }) => {
    const number = index + 1;
    if (enforceRequired && !entry.role) {
      errors[roleFieldName(root, index, 'role')] = `Select the role for role ${number}`;
    }
    if (enforceRequired && !entry.time && !isExclusiveRole(question, entry.role)) {
      errors[roleFieldName(root, index, 'time')] = `Select how much of the staff member’s time the funding is paying for role ${number}`;
    }
    if (entry.role === OTHER_ROLE) {
      if (enforceRequired && !entry.otherRole.trim()) {
        errors[roleFieldName(root, index, 'other')] = `Enter the name of the other role for role ${number}`;
      } else if (entry.otherRole.trim().length > MAX_OTHER_ROLE_LENGTH) {
        errors[roleFieldName(root, index, 'other')] = `The name of the other role for role ${number} must be ${MAX_OTHER_ROLE_LENGTH} characters or less`;
      }
    }
  });

  return errors;
};

// Error-summary items for the repeater's errors: the key names the control, the target is where the link lands.
export const rolesErrorItems = (
  root: string,
  question: Pick<MaturityQuestionDto, 'options' | 'secondaryOptions' | 'exclusiveOptionValue'>,
  errors: Record<string, string>
): { targetName: string; text: string }[] =>
  Object.entries(errors)
    .filter(([key, message]) => key.startsWith(`${root}-`) && Boolean(message))
    .map(([key, message]) => {
      const parsed = parseRoleKey(root, key);
      return { targetName: parsed ? roleErrorTargetId(root, parsed.index, parsed.field, question) : key, text: message };
    });

const labelFor = (options: { value: string; label: string }[], value: string): string => options.find((option) => option.value === value)?.label ?? value;

// Two rows per saved row on the check-your-answers card, one per control, each keyed "Role N: {legend}" (Figma
// 2744-182109). The submitted view and the ZIP export keep the backend's single rendered line — the read-only
// frames are placeholder-only, so there is no design to match there.
export const rolesSummaryRows = (
  question: Pick<MaturityQuestionDto, 'label' | 'options' | 'secondaryOptions' | 'exclusiveOptionValue'>,
  entries: RoleEntry[],
  // The owning SummaryList's testId. Given one, each row states its own id rather than letting SummaryList derive
  // it from the label — the labels are the legends above, so a reworded question would silently move the selector.
  listTestId?: string
): { label: string; value: string; testId?: string }[] => {
  const rows = cleanRoles(entries);
  // The exclusive "None" is one answer, not a role with a time, so it is one row under the question itself.
  if (isExclusiveAnswer(question, rows)) {
    return [{ label: question.label, value: labelFor(question.options, rows[0].role), testId: listTestId ? composeTestId(listTestId, 'row', 'role', 'none') : undefined }];
  }
  return rows.flatMap((entry, index) => {
    const role = entry.role === OTHER_ROLE && entry.otherRole.trim() ? `Other role – ${entry.otherRole.trim()}` : labelFor(question.options, entry.role);
    const rowTestId = (field: RoleField) => (listTestId ? composeTestId(listTestId, 'row', 'role', index + 1, field) : undefined);
    return [
      { label: `Role ${index + 1}: ${ROLE_LEGEND}`, value: role || 'Not provided', testId: rowTestId('role') },
      { label: `Role ${index + 1}: ${ROLE_TIME_LEGEND}`, value: entry.time ? labelFor(question.secondaryOptions, entry.time) : 'Not provided', testId: rowTestId('time') },
    ];
  });
};

// The renderer's question shape for a Roles question — kept out of GovUKDynamicQuestionRenderer, which has no
// repeater: QuestionBlocks renders it through FundedRolesRepeater instead.
export const isRolesQuestion = (question: Pick<FormQuestionDto, 'questionType'>): boolean => question.questionType === 'Roles';
