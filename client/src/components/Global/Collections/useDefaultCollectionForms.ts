import { useEffect, type Dispatch } from 'react';
import type { CollectionFormAction, CollectionFormState } from '../../../views/Admin/Collections/reducer';
import type { CollectionFormOptionDto } from '../../../views/Admin/Collections/queries';

// Ticks every form of the chosen type (BLUM-7943) as soon as the type and its options are known, so the Forms step
// opens preselected and the admin only unticks what they don't want. Shared by CollectionWizard (create, draft
// resume) and CollectionEditor (scheduled/open) so a type change in either lands on the same default. The reducer
// applies it once per type — see APPLY_DEFAULT_FORMS.
export const useDefaultCollectionForms = (
  state: CollectionFormState,
  dispatch: Dispatch<CollectionFormAction>,
  typeFormOptions: CollectionFormOptionDto[],
  optionsLoaded: boolean
): void => {
  const { type, formsDefaultAppliedFor } = state;

  useEffect(() => {
    if (!type || !optionsLoaded || formsDefaultAppliedFor === type) {
      return;
    }
    dispatch({ type: 'APPLY_DEFAULT_FORMS', forType: type, availableIds: typeFormOptions.map((option) => option.moduleId) });
  }, [type, formsDefaultAppliedFor, optionsLoaded, typeFormOptions, dispatch]);
};
