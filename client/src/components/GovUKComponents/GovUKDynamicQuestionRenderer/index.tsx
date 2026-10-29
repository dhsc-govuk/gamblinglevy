import React from 'react';
import { Details } from 'govuk-react';
// From the sibling folders, not the barrel: the barrel re-exports this module, so importing it back was a cycle.
// Harmless in one bundle, but with the views split out (BLUM-8759) this module and the barrel land in different
// chunks, and Rollup warns that a cycle across chunks can break execution order.
import GovUKFieldset from '../GovUKFieldset';
import GovUKRadio from '../GovUKRadio';
import GovUKCheckbox from '../GovUKCheckbox';
import GovUKInputWithSuffix from '../GovUKInputWithSuffix';
import { govukColors } from '../../../styles/govuk-global';

export interface FormQuestion {
  id: string;
  code: string;
  label: string;
  hint?: string | null;
  helpText?: string | null;
  helpTextSummary?: string | null;
  questionType: string | number;
  isRequired: boolean;
  isActive: boolean;
  displayOrder: number;
  conditionalQuestionCode?: string | null;
  conditionalValue?: string | null;
  options: FormQuestionOption[];
  // Numeric metadata from the quarterly-report Question Protocol (BLUM-6611). A suffix ("%" for
  // percentages) renders Number questions via GovUKInputWithSuffix; min/max drive range validation.
  minValue?: number | null;
  maxValue?: number | null;
  suffix?: string | null;
  // Question Protocol copy (BLUM-7620): the message shown when a required question is left blank, the free-text
  // character limit (which turns a Textarea into a GDS character count) and the over-limit message. All three are
  // seeded server-side and mirrored here so the client shows the same wording the API would return.
  errorMessage?: string | null;
  maxLength?: number | null;
  maxLengthErrorMessage?: string | null;
  // A checkbox option that is exclusive of the others (the maturity assessment's "None", BLUM-7855). The
  // activity-record "all residents" rule below predates it and stays keyed on the code.
  exclusiveOptionValue?: string | null;
}

export interface FormQuestionOption {
  value: string;
  label: string;
  displayOrder: number;
}

export interface FormState {
  answers: Record<string, string | undefined>;
}

export interface ValidationErrors {
  [key: string]: string | undefined;
}

export interface GovUKDynamicQuestionRendererProps {
  questions: FormQuestion[];
  answers: Record<string, string | undefined>;
  errors: ValidationErrors;
  onAnswerChange: (questionCode: string, value: string) => void;
  placeholderReplacer?: (text: string) => string;
  controlSize?: 'small';
  checkboxSize?: 'small';
  questionSize?: 'm';
  showQuestionCode?: boolean;
}

interface QuestionRendererProps {
  question: FormQuestion;
  value: string | undefined;
  error: string | undefined;
  onChange: (value: string) => void;
  allQuestions: FormQuestion[];
  answers: Record<string, string | undefined>;
  errors: ValidationErrors;
  onAnswerChange: (questionCode: string, value: string) => void;
  placeholderReplacer: (text: string) => string;
  controlSize?: 'small';
  checkboxSize?: 'small';
  questionSize?: 'm';
  showQuestionCode: boolean;
}

// Visible unless a conditional rule's parent answer does not match. conditionalValue is a comma-separated
// allow-list and a checkbox parent may hold several comma-joined values — kept in step with the backend
// ActivityAnswerValidator so the rendered form and server validation never disagree.
export const isQuestionVisible = (question: FormQuestion, answers: Record<string, string | undefined>): boolean => {
  if (!question.conditionalQuestionCode || !question.conditionalValue) {
    return true;
  }
  const parentValue = answers[question.conditionalQuestionCode];
  if (!parentValue) {
    return false;
  }
  const allowed = question.conditionalValue.split(',').map((v) => v.trim());
  const parentValues = parentValue.split(',').map((v) => v.trim());
  return parentValues.some((v) => allowed.includes(v));
};

const formatHelpText = (text: string): string => {
  return text
    .replace(/\*\*(.*?)\*\*/g, '<strong>$1</strong>')
    .replace(/\n\n/g, '</p><p>')
    .replace(/^/, '<p>')
    .replace(/$/, '</p>');
};

const HelpBlock = ({ question, placeholderReplacer }: { question: FormQuestion; placeholderReplacer: (text: string) => string }): React.ReactElement | null => {
  if (!question.helpTextSummary || !question.helpText) {
    return null;
  }

  const helpTextSummary = placeholderReplacer(question.helpTextSummary);
  const helpText = placeholderReplacer(question.helpText);

  return (
    <div className="govuk-!-margin-bottom-4">
      <Details summary={helpTextSummary}>
        <div dangerouslySetInnerHTML={{ __html: formatHelpText(helpText) }} />
      </Details>
    </div>
  );
};

const QuestionRenderer = ({
  question,
  value,
  error,
  onChange,
  allQuestions,
  answers,
  errors,
  onAnswerChange,
  placeholderReplacer,
  controlSize,
  checkboxSize,
  questionSize,
  showQuestionCode,
}: QuestionRendererProps): React.ReactElement | null => {
  const conditionalChildren = allQuestions.filter((q) => q.conditionalQuestionCode === question.code);

  const childRevealsOn = (child: FormQuestion, triggerValue: string): boolean =>
    !!child.conditionalValue &&
    child.conditionalValue
      .split(',')
      .map((v) => v.trim())
      .includes(triggerValue);

  const renderChildren = (matches: FormQuestion[]): React.ReactNode => {
    if (matches.length === 0) return undefined;

    return (
      <>
        {matches.map((childQuestion) => (
          <QuestionRenderer
            key={childQuestion.id}
            question={childQuestion}
            value={answers[childQuestion.code]}
            error={errors[childQuestion.code]}
            onChange={(v) => onAnswerChange(childQuestion.code, v)}
            allQuestions={allQuestions}
            answers={answers}
            errors={errors}
            onAnswerChange={onAnswerChange}
            placeholderReplacer={placeholderReplacer}
            controlSize={controlSize}
            checkboxSize={checkboxSize}
            questionSize={questionSize}
            showQuestionCode={showQuestionCode}
          />
        ))}
      </>
    );
  };

  const renderConditionalContent = (triggerValue: string): React.ReactNode => renderChildren(conditionalChildren.filter((child) => childRevealsOn(child, triggerValue)));

  const questionLabel = placeholderReplacer(question.label);
  const questionHint = question.hint ? placeholderReplacer(question.hint) : undefined;

  const renderQuestionInput = () => {
    const questionType = String(question.questionType);
    switch (questionType) {
      case 'Text':
      case '0':
        return (
          <GovUKFieldset.Input
            id={question.code.toLowerCase()}
            name={question.code}
            label={questionLabel}
            labelSize={questionSize}
            questionCode={showQuestionCode ? question.code : undefined}
            value={value || ''}
            error={error}
            hint={questionHint}
            onChange={(e) => onChange(e.target.value)}
          />
        );

      case 'Number':
        if (question.suffix) {
          return (
            <GovUKInputWithSuffix
              id={question.code.toLowerCase()}
              name={question.code}
              label={questionLabel}
              suffix={question.suffix}
              type="number"
              questionCode={showQuestionCode ? question.code : undefined}
              value={value || ''}
              error={error}
              hint={questionHint}
              onChange={(e) => onChange(e.target.value)}
            />
          );
        }
        return (
          <GovUKFieldset.Input
            id={question.code.toLowerCase()}
            name={question.code}
            label={questionLabel}
            labelSize={questionSize}
            type="number"
            questionCode={showQuestionCode ? question.code : undefined}
            value={value || ''}
            error={error}
            hint={questionHint}
            onChange={(e) => onChange(e.target.value)}
          />
        );

      // A whole-pound money amount (BLUM-7860). type="text", not "number": GDS requires the user to be able to
      // type "1,500" or "£25" and be told what is wrong with it, and a number input silently swallows both.
      //
      // String-only, like Number and Textarea: the legacy numeric arms below ('0'-'3') exist for an API that
      // serialised QuestionTypes as ints and never emitted these three. A numeric Currency would fall through to
      // default and render no input at all.
      //
      // width-5 is the Figma's compact £ field (Joint partnership "Activity details", BLUM-8113).
      case 'Currency':
        return (
          <GovUKInputWithSuffix
            id={question.code.toLowerCase()}
            name={question.code}
            label={questionLabel}
            labelSize={questionSize}
            prefix="£"
            width="5"
            questionCode={showQuestionCode ? question.code : undefined}
            value={value || ''}
            error={error}
            hint={questionHint}
            onChange={(e) => onChange(e.target.value)}
          />
        );

      case 'Textarea':
        return (
          <GovUKFieldset.Textarea
            id={question.code.toLowerCase()}
            name={question.code}
            label={questionLabel}
            labelSize={questionSize}
            questionCode={showQuestionCode ? question.code : undefined}
            value={value || ''}
            rows={5}
            error={error}
            hint={questionHint}
            maxLength={question.maxLength ?? undefined}
            onChange={(e) => onChange(e.target.value)}
          />
        );

      case 'Radio':
      case '1': {
        const options = question.options
          .sort((a, b) => a.displayOrder - b.displayOrder)
          .map((opt) => ({
            value: opt.value,
            label: opt.label,
            conditional: renderConditionalContent(opt.value),
          }));

        return (
          <GovUKRadio
            id={question.code.toLowerCase()}
            name={question.code}
            legend={questionLabel}
            legendSize={questionSize}
            questionCode={showQuestionCode ? question.code : undefined}
            size={controlSize}
            options={options}
            value={value}
            error={error}
            hint={questionHint}
            onChange={(v) => onChange(String(v))}
          />
        );
      }

      case 'Checkbox':
      case '2': {
        const selectedValues = value ? value.split(',') : [];
        const sortedOptions = [...question.options].sort((a, b) => a.displayOrder - b.displayOrder);

        // A child conditional on SEVERAL of the parent's options is revealed once, not once per matching option.
        // GovUKCheckbox renders every option's conditional content (unticked ones hidden by CSS, the govuk-frontend
        // pattern), so rendering the same child under each match put N copies of its inputs in the DOM sharing one
        // id and — for a radio child — one `name`. The browser allows a single checked radio per name, so React's
        // last write won and left a hidden copy checked: the visible radio looked unselected until it was clicked
        // a second time. Prefer a ticked option as the owner so the reveal sits next to a box the user can see.
        // Only the owner therefore carries aria-controls/aria-expanded, and ownership moves: ticking an earlier
        // matching option takes it over, so a still-ticked later one loses those attributes while the panel it
        // also triggers stays open above it. That is the accepted cost of one reveal — restoring the attributes
        // per option means restoring the duplicate ids and the two-click bug with them.
        const revealOwner = new Map<string, string>();
        conditionalChildren.forEach((child) => {
          const matching = sortedOptions.filter((opt) => childRevealsOn(child, String(opt.value)));
          const owner = matching.find((opt) => selectedValues.includes(String(opt.value))) ?? matching[0];
          if (owner) {
            revealOwner.set(child.code, String(owner.value));
          }
        });

        const options = sortedOptions.map((opt) => ({
          value: opt.value,
          label: opt.label,
          exclusive:
            (question.code.endsWith('_WHO_FOR') && opt.value === 'all_residents') || (question.exclusiveOptionValue != null && opt.value === question.exclusiveOptionValue),
          conditionalContent: renderChildren(conditionalChildren.filter((child) => revealOwner.get(child.code) === String(opt.value))),
        }));

        return (
          <GovUKCheckbox
            id={question.code.toLowerCase()}
            name={question.code}
            legend={questionLabel}
            legendSize={questionSize}
            questionCode={showQuestionCode ? question.code : undefined}
            size={checkboxSize ?? controlSize}
            options={options}
            value={selectedValues}
            error={error}
            hint={questionHint}
            onChange={(values) => onChange(values.join(','))}
          />
        );
      }

      case 'Select':
      case '3': {
        const options = [
          { value: '', label: 'Select an option...' },
          ...question.options
            .sort((a, b) => a.displayOrder - b.displayOrder)
            .map((opt) => ({
              value: opt.value,
              label: opt.label,
            })),
        ];

        const selectConditional = value ? renderConditionalContent(value) : null;
        const conditionalId = conditionalChildren.length > 0 ? `${question.code.toLowerCase()}-conditional` : undefined;

        return (
          <>
            <GovUKFieldset.Select
              id={question.code.toLowerCase()}
              name={question.code}
              label={questionLabel}
              labelSize={questionSize}
              questionCode={showQuestionCode ? question.code : undefined}
              value={value || ''}
              error={error}
              hint={questionHint}
              options={options}
              conditionalId={conditionalId}
              onChange={(e) => onChange(e.target.value)}
            />
            {conditionalId && (
              <div id={conditionalId} aria-live="polite" style={{ borderLeft: `4px solid ${govukColors.grey2}`, paddingLeft: '20px', marginTop: '8px' }}>
                {selectConditional}
              </div>
            )}
          </>
        );
      }

      default:
        return null;
    }
  };

  return (
    <>
      <HelpBlock question={question} placeholderReplacer={placeholderReplacer} />
      {renderQuestionInput()}
    </>
  );
};

const GovUKDynamicQuestionRenderer = ({
  questions,
  answers,
  errors,
  onAnswerChange,
  placeholderReplacer = (text) => text,
  controlSize,
  checkboxSize,
  questionSize,
  showQuestionCode = true,
}: GovUKDynamicQuestionRendererProps): React.ReactElement => {
  const topLevelQuestions = questions.filter((q) => q.isActive && !q.conditionalQuestionCode).sort((a, b) => a.displayOrder - b.displayOrder);

  return (
    <div>
      {topLevelQuestions.map((question, index) => {
        if (!isQuestionVisible(question, answers)) {
          return null;
        }

        return (
          <div key={question.id} className={index > 0 ? 'govuk-!-margin-top-6' : ''}>
            <QuestionRenderer
              question={question}
              value={answers[question.code]}
              error={errors[question.code]}
              onChange={(value) => onAnswerChange(question.code, value)}
              allQuestions={questions}
              answers={answers}
              errors={errors}
              onAnswerChange={onAnswerChange}
              placeholderReplacer={placeholderReplacer}
              controlSize={controlSize}
              checkboxSize={checkboxSize}
              questionSize={questionSize}
              showQuestionCode={showQuestionCode}
            />
          </div>
        );
      })}
    </div>
  );
};

export default GovUKDynamicQuestionRenderer;
