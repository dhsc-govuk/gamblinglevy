import React, { useState } from 'react';
import { ErrorSummary, GovUKFieldset, GovUKButton } from '../../../GovUKComponents';
import type { FormModuleDto } from '../../../../views/Admin/Forms';
import type { CreateFormModuleInput } from '../../../../views/Admin/Forms';
import { govukColors } from '../../../../styles/govuk-global';

// Inline add / edit form for a data-collection form module. The code is the module's immutable
// identifier (locked on edit). Mirrors the activity Form Designer's inline forms — local state,
// onSubmit / onCancel callbacks owned by the parent view.
interface Props {
  module?: FormModuleDto;
  onSubmit: (input: CreateFormModuleInput) => void;
  onCancel: () => void;
  isSubmitting?: boolean;
}

const ModuleForm = ({ module, onSubmit, onCancel, isSubmitting = false }: Props): React.ReactElement => {
  const isEdit = Boolean(module);
  const [code, setCode] = useState(module?.code ?? '');
  const [name, setName] = useState(module?.name ?? '');
  const [description, setDescription] = useState(module?.description ?? '');
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [submitAttempts, setSubmitAttempts] = useState(0);

  const handleSubmit = () => {
    const next: Record<string, string> = {};

    if (!isEdit) {
      if (!code.trim()) {
        next['admin-forms-module-form-code'] = 'Enter a form code';
      } else if (!/^[A-Z0-9_]+$/.test(code.trim())) {
        next['admin-forms-module-form-code'] = 'A code can only contain capital letters, numbers and underscores';
      }
    }
    if (!name.trim()) {
      next['admin-forms-module-form-name'] = 'Enter a form name';
    }

    setSubmitAttempts((attempts) => attempts + 1);
    setErrors(next);
    if (Object.keys(next).length > 0) {
      return;
    }

    onSubmit({ code: code.trim(), name: name.trim(), description: description.trim() ? description.trim() : null });
  };

  const errorList = Object.entries(errors).map(([targetName, text]) => ({ targetName, text }));

  return (
    <div className="govuk-!-margin-bottom-6 govuk-!-padding-4" style={{ border: `1px solid ${govukColors.grey2}` }} data-testid="admin-forms-module-form">
      <ErrorSummary heading="There is a problem" errors={errorList} focusTrigger={submitAttempts} />

      <GovUKFieldset.Input
        id="admin-forms-module-form-code"
        name="admin-forms-module-form-code"
        label="Form code"
        hint="A unique identifier for this form, e.g. SPEND_RETURN. Capital letters, numbers and underscores only. Cannot be changed later."
        value={code}
        error={errors['admin-forms-module-form-code']}
        disabled={isEdit}
        required={!isEdit}
        onChange={(e) => setCode(e.target.value.toUpperCase())}
        testId="admin-forms-module-form-code"
      />

      <GovUKFieldset.Input
        id="admin-forms-module-form-name"
        name="admin-forms-module-form-name"
        label="Form name"
        hint="The name shown to admins managing this form."
        value={name}
        error={errors['admin-forms-module-form-name']}
        required
        onChange={(e) => setName(e.target.value)}
        testId="admin-forms-module-form-name"
      />

      <GovUKFieldset.Textarea
        id="admin-forms-module-form-description"
        name="admin-forms-module-form-description"
        label="Description (optional)"
        rows={3}
        value={description}
        onChange={(e) => setDescription(e.target.value)}
        testId="admin-forms-module-form-description"
      />

      <div className="govuk-button-group">
        <GovUKButton onClick={handleSubmit} disabled={isSubmitting} data-testid="admin-forms-module-form-submit">
          {isEdit ? 'Save changes' : 'Create form'}
        </GovUKButton>
        <button type="button" className="govuk-link" onClick={onCancel} data-testid="admin-forms-module-form-cancel">
          Cancel
        </button>
      </div>
    </div>
  );
};

export default ModuleForm;
