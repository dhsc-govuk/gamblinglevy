import React, { useEffect } from 'react';
import { Details } from 'govuk-react';
import GovUKButton from '../GovUKButton';
import GovUKBackLink from '../GovUKBackLink';
import { composeTestId } from '../../../helpers/testIds';

interface StepperStep {
  label: string;
  component: React.ReactNode;
}

interface GovUKStepperProps {
  step: number;
  setStep: React.Dispatch<React.SetStateAction<number>>;
  stepperSteps: StepperStep[];
  completeLabel: string;
  handleComplete: () => void;
  validate?: (step: number) => boolean;
  isNextDisabled?: boolean;
  title?: string;
  description?: string;
  helpSummary?: string;
  helpContent?: React.ReactNode;
  onSaveAsDraft?: () => void;
  showSaveAsDraft?: boolean;
  onCancel?: () => void;
  testId?: string;
}

const GovUKStepper = ({
  step,
  setStep,
  stepperSteps,
  completeLabel,
  handleComplete,
  validate,
  isNextDisabled = false,
  title,
  description,
  helpSummary,
  helpContent,
  onSaveAsDraft,
  showSaveAsDraft = true,
  onCancel,
  testId,
}: GovUKStepperProps): React.ReactElement => {
  const isLastStep = step === stepperSteps.length - 1;

  useEffect(() => {
    window.scrollTo(0, 0);
  }, [step]);

  const handleNext = () => {
    if (validate && validate(step)) {
      return;
    }
    if (isLastStep) {
      handleComplete();
    } else {
      setStep((prev) => prev + 1);
    }
  };

  const handleBack = () => {
    setStep((prev) => prev - 1);
  };

  return (
    <div data-testid={testId}>
      {step > 0 && (
        <GovUKBackLink onClick={handleBack} data-testid={composeTestId(testId, 'back')}>
          Back
        </GovUKBackLink>
      )}

      <div>
        <p className="govuk-caption-l" data-testid={composeTestId(testId, 'step-counter')}>
          Step {step + 1} of {stepperSteps.length}
        </p>
        {title && <h1 className="govuk-heading-l">{title}</h1>}
        {description && <p className="govuk-body">{description}</p>}
        {helpSummary && helpContent && <Details summary={helpSummary}>{helpContent}</Details>}
      </div>

      <hr className="govuk-section-break govuk-section-break--l govuk-section-break--visible" />

      <div data-testid={composeTestId(testId, 'step', step)}>{stepperSteps[step].component}</div>

      <hr className="govuk-section-break govuk-section-break--l govuk-section-break--visible" />

      <div className="govuk-button-group">
        <GovUKButton
          className="govuk-button"
          disabled={isNextDisabled}
          onClick={handleNext}
          data-testid={composeTestId(testId, isLastStep ? 'complete' : 'continue')}
        >
          {isLastStep ? completeLabel : 'Continue'}
        </GovUKButton>
        {showSaveAsDraft && onSaveAsDraft && (
          <GovUKButton
            className="govuk-button govuk-button--secondary"
            onClick={onSaveAsDraft}
            data-testid={composeTestId(testId, 'save-as-draft')}
          >
            Save as draft and exit
          </GovUKButton>
        )}
        {onCancel && !showSaveAsDraft && (
          <GovUKButton
            className="govuk-button govuk-button--secondary"
            onClick={onCancel}
            data-testid={composeTestId(testId, 'cancel')}
          >
            Cancel
          </GovUKButton>
        )}
      </div>
    </div>
  );
};

export default GovUKStepper;
