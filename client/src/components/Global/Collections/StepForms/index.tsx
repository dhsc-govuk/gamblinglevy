import React from 'react';
import { GovUKFieldset, H2, Paragraph } from '../../../GovUKComponents';
import type { CollectionFormState, CollectionFormAction } from '../../../../views/Admin/Collections/reducer';
import type { CollectionFormOptionDto } from '../../../../views/Admin/Collections/queries';
import { formatGovUkDate } from '../../../../views/Admin/Collections/display';
import useFeature from '../../../../hooks/useFeature';
import { composeTestId } from '../../../../helpers/testIds';

interface Props {
  state: CollectionFormState;
  dispatch: React.Dispatch<CollectionFormAction>;
  formOptions: CollectionFormOptionDto[];
}

export const formVersionFieldId = (moduleId: string): string => composeTestId('admin-collections-create-form-version', moduleId) ?? '';

// "Forms" step of the add-collection wizard — the admin chooses which forms make up the data collection: form
// modules for a quarterly report, catalogue sections for a maturity assessment (BLUM-7943). Options arrive already
// filtered to the collection's type and all ticked by default (useDefaultCollectionForms). A form module surfaces
// when it was last changed so the admin can confirm they are including the current version; a maturity section has
// no change date, so it shows no hint.
//
// BLUM-9103 (behind BLUM_9087): for each selected form that has saved versions, the admin can choose which version
// this collection uses. "Latest version" takes the newest when the collection is published.
const StepForms = ({ state, dispatch, formOptions }: Props): React.ReactElement => {
  const versionsEnabled = useFeature('BLUM_9087');
  const versionedSelections = versionsEnabled ? formOptions.filter((option) => state.formModuleIds.includes(option.moduleId) && (option.versions?.length ?? 0) > 0) : [];

  return (
    <>
      <GovUKFieldset.CheckboxGroup
        name="admin-collections-create-forms"
        legend="Which forms do you want to include in this data collection?"
        legendSize="m"
        options={formOptions.map((option) => ({
          value: option.moduleId,
          text: option.name,
          hint: option.lastChangedAt ? `Last changed on ${formatGovUkDate(option.lastChangedAt)}` : undefined,
        }))}
        value={state.formModuleIds}
        error={state.errors['forms']}
        size="small"
        onChange={(value) => dispatch({ type: 'TOGGLE_FORM', id: value })}
        testId="admin-collections-create-forms"
      />

      {versionedSelections.length > 0 && (
        <div data-testid="admin-collections-create-form-versions">
          <H2 className="govuk-heading-m">Form versions</H2>
          <Paragraph>
            {Object.keys(state.recordedFormVersions).length > 0
              ? 'Choose which version of each form this data collection uses. Choosing the latest version uses the newest version when you save.'
              : 'Choose which version of each form this data collection uses. The latest version is the newest one when you publish the data collection.'}
          </Paragraph>
          {versionedSelections.map((option) => {
            const recorded = state.recordedFormVersions[option.moduleId];
            return (
              <GovUKFieldset.Select
                key={option.moduleId}
                id={formVersionFieldId(option.moduleId)}
                name={formVersionFieldId(option.moduleId)}
                label={`Version of ${option.name}`}
                labelSize="s"
                hint={recorded ? `This data collection currently uses version ${recorded}.` : undefined}
                value={state.formVersions[option.moduleId] ?? ''}
                options={[
                  { value: '', label: 'Latest version' },
                  ...(option.versions ?? []).map((version) => ({
                    value: String(version.versionNumber),
                    label: `Version ${version.versionNumber}, published ${formatGovUkDate(version.publishedAt)}`,
                  })),
                ]}
                onChange={(event) => dispatch({ type: 'SET_FORM_VERSION', id: option.moduleId, value: event.target.value })}
              />
            );
          })}
        </div>
      )}
    </>
  );
};

export default StepForms;
