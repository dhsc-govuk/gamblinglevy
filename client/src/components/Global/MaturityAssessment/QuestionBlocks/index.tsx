import React from 'react';
import { GovUKDynamicQuestionRenderer } from '../../../GovUKComponents';
import { composeTestId, slugifyTestId } from '../../../../helpers/testIds';
import FundedRolesRepeater from '../FundedRolesRepeater';
import { pageBlocks, toPageFormQuestion } from '../../../../views/MaturityAssessment/display';
import type { MaturityPageDto } from '../../../../views/MaturityAssessment/queries';
import type { RoleEntry } from '../../../../views/MaturityAssessment/roles';
import './styles.css';

interface QuestionBlocksProps {
  page: MaturityPageDto;
  answers: Record<string, string>;
  errors: Record<string, string>;
  onAnswerChange: (questionCode: string, value: string) => void;
  roles: RoleEntry[];
  onRolesChange: (entries: RoleEntry[], removedIndex?: number) => void;
  testId: string;
}

// Renders one maturity page (BLUM-7855): ordinary runs through the shared dynamic renderer, each matrix under its
// own heading as a radio group per item (the "one radio group per item" decision), and the funded-roles question
// through the repeater. The blocks come from pageBlocks so the check page groups exactly the same way.
const QuestionBlocks = ({ page, answers, errors, onAnswerChange, roles, onRolesChange, testId }: QuestionBlocksProps): React.ReactElement => (
  <div className="maturity-question-blocks" data-testid={testId}>
    {pageBlocks(page).map((block) => {
      if (block.kind === 'roles') {
        return (
          <FundedRolesRepeater
            key={block.key}
            question={block.question}
            root={block.question.code.toLowerCase()}
            entries={roles}
            errors={errors}
            onChange={onRolesChange}
            testId={`${testId}-roles`}
          />
        );
      }

      if (block.kind === 'group') {
        return (
          <fieldset className="govuk-fieldset maturity-question-blocks__group" key={block.key} data-testid={composeTestId(testId, 'group', slugifyTestId(block.key))}>
            <legend className="govuk-fieldset__legend govuk-fieldset__legend--m">
              <h2 className="govuk-fieldset__heading">{block.label}</h2>
            </legend>
            <GovUKDynamicQuestionRenderer
              questions={block.questions.map(toPageFormQuestion)}
              answers={answers}
              errors={errors}
              onAnswerChange={onAnswerChange}
              showQuestionCode={false}
            />
          </fieldset>
        );
      }

      return (
        <div className="maturity-question-blocks__questions" key={block.key}>
          <GovUKDynamicQuestionRenderer
            questions={block.questions.map(toPageFormQuestion)}
            answers={answers}
            errors={errors}
            onAnswerChange={onAnswerChange}
            questionSize="m"
            showQuestionCode={false}
          />
        </div>
      );
    })}
  </div>
);

export default QuestionBlocks;
