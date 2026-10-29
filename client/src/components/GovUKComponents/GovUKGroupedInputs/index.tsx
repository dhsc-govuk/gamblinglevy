import React from 'react';
import GovUKInputWithSuffix from '../GovUKInputWithSuffix';
import { composeTestId } from '../../../helpers/testIds';
import './styles.css';

interface GroupedInputItem {
  code: string;
  label: string;
  value: string;
  suffix?: string;
  prefix?: string;
  error?: string;
  testId?: string;
}

interface GovUKGroupedInputsProps {
  legend: string;
  hint?: string;
  questionCode?: string;
  items: GroupedInputItem[];
  onChange: (code: string, value: string) => void;
  testId?: string;
  stacked?: boolean;
}

const GovUKGroupedInputs: React.FC<GovUKGroupedInputsProps> = ({ legend, hint, questionCode, items, onChange, testId, stacked = false }) => {
  return (
    <div className={`govuk-grouped-inputs${stacked ? ' govuk-grouped-inputs--stacked' : ''}`} data-testid={testId}>
      <fieldset className="govuk-fieldset">
        <legend className="govuk-fieldset__legend govuk-fieldset__legend--s">
          <div className="govuk-grouped-inputs__header">
            <span className="govuk-grouped-inputs__legend">{legend}</span>
            {questionCode && <span className="govuk-grouped-inputs__code">{questionCode}</span>}
          </div>
        </legend>
        {hint && <div className="govuk-hint">{hint}</div>}
        <hr aria-hidden="true" className="govuk-section-break govuk-section-break--visible govuk-!-margin-top-2 govuk-!-margin-bottom-3" />
        <div className="govuk-grouped-inputs__items">
          {items.map((item) => (
            <div key={item.code} className="govuk-grouped-inputs__item">
              {/* Real <label> (not a span) so the per-row input has a programmatic accessible name (WCAG 2.2 AA). */}
              <label className="govuk-grouped-inputs__item-label" htmlFor={item.code}>
                {!stacked && <span aria-hidden="true">— </span>}
                {item.label}
              </label>
              <div className="govuk-grouped-inputs__item-input">
                <GovUKInputWithSuffix
                  id={item.code}
                  name={item.code}
                  label=""
                  prefix={item.prefix}
                  suffix={item.suffix}
                  error={item.error}
                  value={item.value}
                  width={stacked ? '10' : '5'}
                  testId={item.testId ?? composeTestId(testId, 'input', item.code) ?? item.code}
                  onChange={(e: React.ChangeEvent<HTMLInputElement>) => onChange(item.code, e.target.value)}
                />
              </div>
            </div>
          ))}
        </div>
      </fieldset>
    </div>
  );
};

export default GovUKGroupedInputs;
