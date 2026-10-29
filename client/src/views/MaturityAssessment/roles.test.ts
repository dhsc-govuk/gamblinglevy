import { describe, expect, it } from 'vitest';
import {
  firstRoleOption,
  isExclusiveAnswer,
  isHiddenRoleControl,
  pruneRoleErrors,
  rolesErrorItems,
  rolesSummaryRows,
  serialiseRoles,
  validateRoles,
  type RoleEntry,
} from './roles';

// "None" on the funded-roles repeater (BLUM-9112): the question's exclusiveOptionValue. The backend rules these mirror
// are pinned in api/tests/Application.Tests.Unit/MaturityAssessments/MaturityAnswerValidatorTests.cs and
// MaturityRolesAnswerTests.cs: the exclusive row stands alone, asks no time and is stored as one row with an empty time.

const question = {
  label: 'Which roles are being funded by the levy?',
  isRequired: true,
  errorMessage: 'Add at least one role funded by the levy',
  options: [
    { value: 'public_health_officer', label: 'Public Health Officer' },
    { value: 'other', label: 'Other role' },
    { value: 'none', label: 'None' },
  ],
  secondaryOptions: [{ value: '81_100', label: '81% to 100%' }],
  exclusiveOptionValue: 'none',
};

const withoutExclusive = { ...question, exclusiveOptionValue: null };

const root = 'ma_funded_roles';
const none: RoleEntry = { role: 'none', time: '', otherRole: '' };
const officer: RoleEntry = { role: 'public_health_officer', time: '81_100', otherRole: '' };
const other: RoleEntry = { role: 'other', time: '81_100', otherRole: 'Data analyst' };

describe('the funded-roles exclusive "None" answer', () => {
  it('is the exclusive option as the only row', () => {
    expect(isExclusiveAnswer(question, [none])).toBe(true);
    expect(isExclusiveAnswer(question, [officer])).toBe(false);
    expect(isExclusiveAnswer(question, [none, officer])).toBe(false);
    expect(isExclusiveAnswer(question, [])).toBe(false);
    // Judged on the rows as shown: with a second (blank) row the exclusive option is not on offer, so not the answer.
    expect(isExclusiveAnswer(question, [none, { role: '', time: '', otherRole: '' }])).toBe(false);
  });

  it('is only exclusive when the question declares it', () => {
    expect(isExclusiveAnswer(withoutExclusive, [none])).toBe(false);
  });

  it('is serialised with no time or other-role name, even when the hidden controls still hold them', () => {
    expect(serialiseRoles(question, [{ ...none, time: '81_100', otherRole: 'Data analyst' }])).toBe('[{"role":"none","time":"","otherRole":null}]');
  });

  it('asks for no time on Continue', () => {
    expect(validateRoles(root, question, [none], { enforceRequired: true })).toEqual({});
  });

  it('is asked for a time like any role on a question without an exclusive option', () => {
    expect(validateRoles(root, withoutExclusive, [none], { enforceRequired: true })).toHaveProperty(`${root}-1-time`);
  });

  it('still asks a real role for its time', () => {
    expect(validateRoles(root, question, [{ ...officer, time: '' }], { enforceRequired: true })).toHaveProperty(`${root}-1-time`);
  });

  it('is one summary row under the question, not a role with a time', () => {
    expect(rolesSummaryRows(question, [none], 'check')).toEqual([{ label: question.label, value: 'None', testId: 'check-row-role-none' }]);
  });
});

describe('firstRoleOption', () => {
  // Every row shows it, so focus on a new row and a role error's summary link always land on a rendered radio.
  it('skips the exclusive option wherever it sits', () => {
    expect(firstRoleOption({ ...question, options: [question.options[2], ...question.options.slice(0, 2)] })?.value).toBe('public_health_officer');
    expect(rolesErrorItems(root, { ...question, options: [question.options[2], ...question.options.slice(0, 2)] }, { [`${root}-2-role`]: 'x' })).toEqual([
      { targetName: `${root}-2-role-public_health_officer`, text: 'x' },
    ]);
  });
});

// Errors on controls the repeater is no longer showing are dropped as the rows change, so the summary never links to
// nothing.
describe('isHiddenRoleControl', () => {
  it('hides the time and other-role name of the exclusive row, never its role', () => {
    expect(isHiddenRoleControl(question, [none], root, `${root}-1-time`)).toBe(true);
    expect(isHiddenRoleControl(question, [none], root, `${root}-1-other`)).toBe(true);
    expect(isHiddenRoleControl(question, [none], root, `${root}-1-role`)).toBe(false);
  });

  it('hides the other-role name once the role is no longer "Other role"', () => {
    expect(isHiddenRoleControl(question, [officer], root, `${root}-1-other`)).toBe(true);
    expect(isHiddenRoleControl(question, [other], root, `${root}-1-other`)).toBe(false);
    expect(isHiddenRoleControl(question, [officer], root, `${root}-1-time`)).toBe(false);
  });

  it('hides every control of a row that no longer exists, but not the one row the repeater always shows', () => {
    expect(isHiddenRoleControl(question, [officer], root, `${root}-2-role`)).toBe(true);
    expect(isHiddenRoleControl(question, [], root, `${root}-1-role`)).toBe(false);
  });

  it('leaves keys that are not repeater controls alone', () => {
    expect(isHiddenRoleControl(question, [none], root, 'MA_OTHER_ROLES')).toBe(false);
    expect(isHiddenRoleControl(question, [none], root, 'submit')).toBe(false);
  });
});

describe('pruneRoleErrors', () => {
  const b: RoleEntry = { role: 'public_health_officer', time: '', otherRole: '' };
  const c: RoleEntry = { role: '', time: '81_100', otherRole: '' };

  it('drops every repeater error on and below a removed row, since those rows moved up one', () => {
    const errors = { [`${root}-2-time`]: 'Select … role 2', [`${root}-3-role`]: 'Select the role for role 3', MA_OTHER_ROLES: 'Too long' };

    // Rows were [officer, b, c]; Remove role 1 leaves [b, c].
    expect(pruneRoleErrors(question, root, errors, [b, c], 0)).toEqual({ MA_OTHER_ROLES: 'Too long' });
    // Remove role 3 keeps role 2's error, which still describes role 2.
    expect(pruneRoleErrors(question, root, errors, [officer, b], 2)).toEqual({ [`${root}-2-time`]: 'Select … role 2', MA_OTHER_ROLES: 'Too long' });
  });

  it('drops errors on controls the rows no longer show, and returns the same object when nothing changes', () => {
    const errors = { [`${root}-1-time`]: 'Select …', [`${root}-1-other`]: 'Enter …' };

    expect(pruneRoleErrors(question, root, errors, [none])).toEqual({});
    expect(pruneRoleErrors(question, root, errors, [officer])).toEqual({ [`${root}-1-time`]: 'Select …' });
    expect(pruneRoleErrors(question, root, errors, [other])).toBe(errors);
  });
});
