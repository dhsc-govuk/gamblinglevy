import { describe, expect, it } from 'vitest';
import {
  buildCreateInput,
  buildUpdateInput,
  collectionFormReducer,
  formVersionShown,
  hydrateCollectionState,
  initialCollectionFormState,
  type CollectionFormState,
} from './reducer';
import type { CollectionDto } from './queries';

// BLUM-9103: the per-form version choice in the collection wizard.
const quarterly = (overrides: Partial<CollectionFormState> = {}): CollectionFormState => ({
  ...initialCollectionFormState,
  type: 'quarterly_report',
  formModuleIds: ['form-a', 'form-b'],
  ...overrides,
});

describe('form version choice', () => {
  it('sends a version per selected form only when the choice is offered', () => {
    const state = quarterly({ formVersions: { 'form-a': '2' } });

    expect(buildCreateInput(state, false).formVersions).toBeUndefined();
    expect(buildCreateInput(state, false, true).formVersions).toEqual([
      { moduleId: 'form-a', versionNumber: 2 },
      { moduleId: 'form-b', versionNumber: null },
    ]);
    expect(buildUpdateInput(state, false, true).formVersions).toEqual(buildCreateInput(state, false, true).formVersions);
  });

  it('sends no versions for a maturity assessment, whose sections have none', () => {
    const state = quarterly({ type: 'maturity_assessment', formVersions: { 'form-a': '2' } });

    expect(buildCreateInput(state, false, true).formVersions).toBeUndefined();
  });

  it('records a choice and forgets every choice when the collection type changes', () => {
    const chosen = collectionFormReducer(quarterly(), { type: 'SET_FORM_VERSION', id: 'form-a', value: '1' });
    expect(chosen.formVersions).toEqual({ 'form-a': '1' });

    const switched = collectionFormReducer(chosen, { type: 'SET_TYPE', value: 'maturity_assessment' });
    expect(switched.formVersions).toEqual({});
  });

  it('forgets the recorded versions too when the collection type changes', () => {
    const saved = quarterly({ recordedFormVersions: { 'form-a': 1 }, recordedVersionChosen: { 'form-a': true } });

    const switched = collectionFormReducer(saved, { type: 'SET_TYPE', value: 'maturity_assessment' });

    expect(switched.recordedFormVersions).toEqual({});
    expect(switched.recordedVersionChosen).toEqual({});
  });

  it('shows on check answers the version the save will record', () => {
    const saved = quarterly({
      formVersions: { 'form-a': '1', 'form-b': '' },
      recordedFormVersions: { 'form-a': 1, 'form-b': 3 },
      recordedVersionChosen: { 'form-a': true, 'form-b': false },
    });

    // A chosen version is kept while it stays chosen.
    expect(formVersionShown(saved, 'form-a')).toBe('1');
    // "Latest version" keeps the version recorded at publish: a later version does not change the collection.
    expect(formVersionShown(saved, 'form-b')).toBe(3);
    // "Latest version" on a version DHSC had chosen clears it, so the save records the newest, not the recorded 1.
    expect(formVersionShown(collectionFormReducer(saved, { type: 'SET_FORM_VERSION', id: 'form-a', value: '' }), 'form-a')).toBeUndefined();
    // A form with nothing recorded (a draft collection) takes the latest.
    expect(formVersionShown(quarterly(), 'form-a')).toBeUndefined();
  });

  it('shows a chosen version as chosen, and a recorded latest as "latest version" with its number kept as a hint', () => {
    const dto = {
      forms: [
        { moduleId: 'form-a', code: 'a', name: 'A', versionNumber: 1, versionChosen: true },
        { moduleId: 'form-b', code: 'b', name: 'B', versionNumber: 3, versionChosen: false },
        { moduleId: 'form-c', code: 'c', name: 'C', versionNumber: null, versionChosen: false },
      ],
      localAuthorities: [],
      reminders: [],
    } as unknown as CollectionDto;

    const hydrated = hydrateCollectionState(dto);

    expect(hydrated.formVersions).toEqual({ 'form-a': '1', 'form-b': '', 'form-c': '' });
    expect(hydrated.recordedFormVersions).toEqual({ 'form-a': 1, 'form-b': 3 });
    // Check answers needs to know form-a's version was chosen: "Latest version" there clears it rather than keeping 1.
    expect(hydrated.recordedVersionChosen).toEqual({ 'form-a': true, 'form-b': false, 'form-c': false });
  });
});
