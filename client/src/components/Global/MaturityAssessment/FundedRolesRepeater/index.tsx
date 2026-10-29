import React, { useEffect, useState } from 'react';
import { GovUKButton, GovUKFieldset, type RadioOption } from '../../../GovUKComponents';
import { composeTestId } from '../../../../helpers/testIds';
import {
  emptyRole,
  firstRoleOption,
  isExclusiveAnswer,
  isExclusiveRole,
  MAX_ROLES,
  OTHER_ROLE,
  ROLE_LEGEND,
  ROLE_TIME_LEGEND,
  roleFieldName,
  type RoleEntry,
} from '../../../../views/MaturityAssessment/roles';
import type { MaturityQuestionDto } from '../../../../views/MaturityAssessment/queries';
import './styles.css';

interface FundedRolesRepeaterProps {
  question: MaturityQuestionDto;
  // The DOM id root every row's controls derive from (the lower-cased question code, like every other dynamic control).
  root: string;
  entries: RoleEntry[];
  // Keyed by control name (roleFieldName) — see validateRoles.
  errors: Record<string, string>;
  // removedIndex is set when the change is a Remove, so the page can drop the errors of the rows that moved up.
  onChange: (entries: RoleEntry[], removedIndex?: number) => void;
  testId: string;
}

// "Which roles are being funded by the levy?" (Figma 2744-182477): a repeating block of role + time radio groups
// with an "Other role" text reveal, a Remove beside each "Role N" heading and "Add another role" underneath.
// Controlled — the page owns the rows and serialises them into the question's single JSON answer on save.
//
// The question's exclusive option — "None" (BLUM-9112), its exclusiveOptionValue — is offered only while there is a
// single row: it answers the question on its own, so choosing it hides the row's time question and "Add another role".
// It hides rather than clears them — with native radios an arrow key selects as it moves, so passing over None must
// not wipe a time or other-role name the user has entered; serialiseRoles is what leaves them out of a saved None.
//
// Focus is managed by hand because the DOM changes under the user: a new row receives focus on its first radio so
// a keyboard or screen-reader user lands on what they just asked for, and removing a row moves focus to the Add
// button rather than letting it fall to <body>.
const FundedRolesRepeater = ({ question, root, entries, errors, onChange, testId }: FundedRolesRepeaterProps): React.ReactElement => {
  const rows = entries.length > 0 ? entries : [emptyRole()];
  const isExclusive = isExclusiveAnswer(question, rows);
  const addButtonId = `${root}-add`;
  const [focusRequest, setFocusRequest] = useState<{ kind: 'row'; index: number } | { kind: 'add' } | null>(null);

  useEffect(() => {
    if (!focusRequest) {
      return;
    }
    if (focusRequest.kind === 'add') {
      document.getElementById(addButtonId)?.focus();
    } else {
      const first = firstRoleOption(question);
      const target = first ? document.getElementById(`${roleFieldName(root, focusRequest.index, 'role')}-${first.value}`) : null;
      target?.focus();
    }
    setFocusRequest(null);
  }, [addButtonId, focusRequest, question, root]);

  const update = (index: number, patch: Partial<RoleEntry>) => {
    onChange(rows.map((row, i) => (i === index ? { ...row, ...patch } : row)));
  };

  const add = () => {
    onChange([...rows, emptyRole()]);
    setFocusRequest({ kind: 'row', index: rows.length });
  };

  const remove = (index: number) => {
    const next = rows.filter((_, i) => i !== index);
    onChange(next.length > 0 ? next : [emptyRole()], index);
    setFocusRequest({ kind: 'add' });
  };

  const roleOptions = (index: number, row: RoleEntry): RadioOption[] =>
    question.options
      .filter((option) => !isExclusiveRole(question, option.value) || rows.length === 1)
      .map((option) => ({
        value: option.value,
        text: option.label,
        conditionalContent:
          option.value === OTHER_ROLE ? (
            <GovUKFieldset.Input
              id={roleFieldName(root, index, 'other')}
              name={roleFieldName(root, index, 'other')}
              label="Name of the other role"
              value={row.otherRole}
              error={errors[roleFieldName(root, index, 'other')]}
              width="20"
              onChange={(event) => update(index, { otherRole: event.target.value })}
              testId={composeTestId(testId, 'row', index + 1, 'other')}
            />
          ) : undefined,
      }));

  const timeOptions: RadioOption[] = question.secondaryOptions.map((option) => ({ value: option.value, text: option.label }));

  return (
    <fieldset className="govuk-fieldset maturity-roles" aria-describedby={question.hint ? `${root}-hint` : undefined} data-testid={testId}>
      <legend className="govuk-fieldset__legend govuk-fieldset__legend--m">
        <h2 className="govuk-fieldset__heading">{question.label}</h2>
      </legend>
      {question.hint && (
        <div className="govuk-hint" id={`${root}-hint`}>
          {question.hint}
        </div>
      )}

      {rows.map((row, index) => (
        <div className="maturity-roles__row" key={roleFieldName(root, index, 'role')} data-testid={composeTestId(testId, 'row', index + 1)}>
          <div className="maturity-roles__row-header">
            <h3 className="govuk-heading-s maturity-roles__row-heading">Role {index + 1}</h3>
            {rows.length > 1 && (
              <GovUKButton
                type="button"
                className="govuk-button govuk-button--secondary maturity-roles__remove"
                onClick={() => remove(index)}
                data-testid={composeTestId(testId, 'row', index + 1, 'remove')}>
                Remove<span className="govuk-visually-hidden"> role {index + 1}</span>
              </GovUKButton>
            )}
          </div>

          <GovUKFieldset.RadioGroup
            name={roleFieldName(root, index, 'role')}
            legend={ROLE_LEGEND}
            visuallyHiddenText={`(role ${index + 1})`}
            options={roleOptions(index, row)}
            value={row.role}
            error={errors[roleFieldName(root, index, 'role')]}
            onChange={(value) => update(index, { role: value })}
            testId={composeTestId(testId, 'row', index + 1, 'role')}
          />

          {!isExclusiveRole(question, row.role) && (
            <GovUKFieldset.RadioGroup
              name={roleFieldName(root, index, 'time')}
              legend={ROLE_TIME_LEGEND}
              visuallyHiddenText={`(role ${index + 1})`}
              options={timeOptions}
              value={row.time}
              error={errors[roleFieldName(root, index, 'time')]}
              onChange={(value) => update(index, { time: value })}
              testId={composeTestId(testId, 'row', index + 1, 'time')}
            />
          )}
        </div>
      ))}

      {rows.length < MAX_ROLES && !isExclusive && (
        <GovUKButton id={addButtonId} type="button" className="govuk-button govuk-button--secondary" onClick={add} data-testid={composeTestId(testId, 'add')}>
          Add another role
        </GovUKButton>
      )}
    </fieldset>
  );
};

export default FundedRolesRepeater;
