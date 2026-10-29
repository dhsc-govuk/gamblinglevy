import React, { useState } from 'react';
import { ErrorSummary, GovUKFieldset, GovUKButton, H3 } from '../../../GovUKComponents';

interface CategoryOption {
  id: string;
  name: string;
}

interface Props {
  heading: string;
  submitLabel: string;
  initialTitle?: string;
  initialDescription?: string;
  initialCategoryId?: string | null;
  categories?: CategoryOption[];
  isLoading?: boolean;
  onSubmit: (input: { title: string; description: string | null; categoryId: string | null }) => void;
  onCancel: () => void;
}

const SectionForm = ({
  heading,
  submitLabel,
  initialTitle = '',
  initialDescription = '',
  initialCategoryId = null,
  categories = [],
  isLoading = false,
  onSubmit,
  onCancel,
}: Props): React.ReactElement => {
  const [title, setTitle] = useState(initialTitle);
  const [description, setDescription] = useState(initialDescription);
  const [categoryId, setCategoryId] = useState(initialCategoryId ?? '');
  const [error, setError] = useState<string | undefined>(undefined);
  const [submitAttempts, setSubmitAttempts] = useState(0);

  const handleSubmit = () => {
    setSubmitAttempts((attempts) => attempts + 1);
    if (!title.trim()) {
      setError('Enter a section title');
      return;
    }
    setError(undefined);
    onSubmit({ title: title.trim(), description: description.trim() || null, categoryId: categoryId || null });
  };

  return (
    <div className="govuk-!-margin-bottom-6 govuk-!-padding-4" style={{ border: '1px solid #b1b4b6' }} data-testid="form-section-form">
      <ErrorSummary heading="There is a problem" errors={error ? [{ targetName: 'form-section-title', text: error }] : []} focusTrigger={submitAttempts} />

      <H3>{heading}</H3>
      <GovUKFieldset.Input
        id="form-section-title"
        name="form-section-title"
        label="Section title"
        value={title}
        error={error}
        required
        onChange={(e) => setTitle(e.target.value)}
      />
      <GovUKFieldset.Select
        id="form-section-category"
        name="form-section-category"
        label="Category (optional)"
        hint="Scope this section to one activity category, or leave as ‘All categories’ to show it everywhere."
        value={categoryId}
        options={[{ value: '', label: 'All categories' }, ...categories.map((category) => ({ value: category.id, label: category.name }))]}
        onChange={(e) => setCategoryId(e.target.value)}
      />
      <GovUKFieldset.Textarea
        id="form-section-description"
        name="form-section-description"
        label="Description (optional)"
        rows={3}
        value={description}
        onChange={(e) => setDescription(e.target.value)}
      />
      <div className="govuk-button-group">
        <GovUKButton onClick={handleSubmit} disabled={isLoading} data-testid="form-section-form-save">
          {submitLabel}
        </GovUKButton>
        <button type="button" className="govuk-link" onClick={onCancel} data-testid="form-section-form-cancel">
          Cancel
        </button>
      </div>
    </div>
  );
};

export default SectionForm;
