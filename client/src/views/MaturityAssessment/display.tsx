import React from 'react';
import { isQuestionVisible } from '../../components/GovUKComponents';
import type { FormQuestionDto } from '../../types/formSchema';
import { renderReportAnswer } from '../Reports/display';
import { parseRoles, rolesSummaryRows } from './roles';
import type { MaturityAnswerDto, MaturityAnswerSource, MaturityPageDto, MaturityQuestionDto, MaturitySnapshotPageDto } from './queries';

// Shared maturity-assessment presentation/copy helpers — the assessment's display name, the renderer's question
// shape, how a page is split into rendering blocks and how those blocks read on check-your-answers and the
// read-only views.

export { maturityAssessmentName, maturityPeriodValue } from './names';

// Reshapes a maturity question into the FormQuestionDto shape consumed by GovUKDynamicQuestionRenderer,
// validateAnswers and collectAnswers. The page slug stands in for the section id.
export const toPageFormQuestion = (question: MaturityQuestionDto): FormQuestionDto => ({
  id: question.id,
  sectionId: question.page,
  code: question.code,
  label: question.label,
  hint: question.hint,
  helpText: null,
  helpTextSummary: null,
  questionType: question.questionType,
  isRequired: question.isRequired,
  isActive: true,
  displayOrder: question.displayOrder,
  conditionalQuestionCode: question.conditionalQuestionCode,
  conditionalValue: question.conditionalValue,
  options: question.options.map((option, index) => ({ value: option.value, label: option.label, displayOrder: index })),
  minValue: null,
  maxValue: null,
  suffix: null,
  errorMessage: question.errorMessage,
  maxLength: question.maxLength,
  maxLengthErrorMessage: question.maxLengthErrorMessage,
  exclusiveOptionValue: question.exclusiveOptionValue,
});

export const maturityAnswersToRecord = (answers: MaturityAnswerDto[]): Record<string, string> =>
  answers.reduce<Record<string, string>>((acc, answer) => {
    if (answer.value != null) {
      acc[answer.questionCode] = answer.value;
    }
    return acc;
  }, {});

// How a page renders (BLUM-7855): runs of ordinary questions go through the dynamic renderer; a matrix — the
// consecutive radio items sharing a groupLabel — sits under one heading; the funded-roles question is the repeater.
// Conditional children travel with the block of the parent that reveals them, so the renderer sees both.
export type PageBlock =
  | { kind: 'questions'; key: string; questions: MaturityQuestionDto[] }
  | { kind: 'group'; key: string; label: string; questions: MaturityQuestionDto[] }
  | { kind: 'roles'; key: string; question: MaturityQuestionDto };

export const pageBlocks = (page: MaturityPageDto): PageBlock[] => {
  const ordered = [...page.questions].sort((a, b) => a.displayOrder - b.displayOrder);
  const blocks: PageBlock[] = [];
  const blockOf = new Map<string, PageBlock>();

  ordered.forEach((question) => {
    // A conditional child joins its parent's block (the parent always precedes it in display order).
    const parentBlock = question.conditionalQuestionCode ? blockOf.get(question.conditionalQuestionCode) : undefined;
    if (parentBlock && parentBlock.kind !== 'roles') {
      parentBlock.questions.push(question);
      blockOf.set(question.code, parentBlock);
      return;
    }

    if (question.questionType === 'Roles') {
      const block: PageBlock = { kind: 'roles', key: question.code, question };
      blocks.push(block);
      blockOf.set(question.code, block);
      return;
    }

    const last = blocks[blocks.length - 1];
    if (question.groupLabel) {
      if (last?.kind === 'group' && last.label === question.groupLabel) {
        last.questions.push(question);
        blockOf.set(question.code, last);
        return;
      }
      const block: PageBlock = { kind: 'group', key: question.code, label: question.groupLabel, questions: [question] };
      blocks.push(block);
      blockOf.set(question.code, block);
      return;
    }

    if (last?.kind === 'questions') {
      last.questions.push(question);
      blockOf.set(question.code, last);
      return;
    }
    const block: PageBlock = { kind: 'questions', key: question.code, questions: [question] };
    blocks.push(block);
    blockOf.set(question.code, block);
  });

  return blocks;
};

// A row of the check-your-answers / read-only summary lists. Structurally SummaryList's item shape: a key-only row
// carries no value; isSubAnswer indents a matrix item under its group heading.
export interface MaturitySummaryRow {
  label: string;
  value?: React.ReactNode;
  isSubAnswer?: boolean;
  testId?: string;
}

// The visible questions of one page as summary-list rows: matrix items nest under a key-only row carrying the
// group question; the repeater lists one row per role.
export const checkPageRows = (page: MaturityPageDto, answers: Record<string, string>, listTestId?: string): MaturitySummaryRow[] =>
  pageBlocks(page).flatMap((block): MaturitySummaryRow[] => {
    if (block.kind === 'roles') {
      const rows = rolesSummaryRows(block.question, parseRoles(answers[block.question.code]), listTestId);
      return rows.length > 0 ? rows : [{ label: block.question.label, value: 'Not provided' }];
    }

    const visible = block.questions.map(toPageFormQuestion).filter((question) => isQuestionVisible(question, answers));
    const rows = visible.map((question) => ({ label: question.label, value: renderReportAnswer(question, answers[question.code]) }));

    if (block.kind === 'group' && rows.length > 0) {
      return [{ label: block.label }, ...rows.map((row) => ({ ...row, isSubAnswer: true }))];
    }
    return rows;
  });

const URL_PATTERN = /^https?:\/\//;

// The frozen snapshot's rows for one page, grouped the same way. Answers are already rendered server-side; a bare
// web address is still made a link, as the check page does.
export const readOnlyPageRows = (page: MaturitySnapshotPageDto): MaturitySummaryRow[] => {
  const rows: MaturitySummaryRow[] = [];
  let currentGroup: string | null = null;

  page.rows.forEach((row) => {
    const value: React.ReactNode =
      row.answer.length === 0 ? (
        'Not provided'
      ) : URL_PATTERN.test(row.answer.trim()) && !/\s/.test(row.answer.trim()) ? (
        <a className="govuk-link" href={row.answer.trim()}>
          {row.answer.trim()}
        </a>
      ) : (
        row.answer
      );

    if (row.groupLabel) {
      if (currentGroup !== row.groupLabel) {
        currentGroup = row.groupLabel;
        rows.push({ label: row.groupLabel });
      }
      rows.push({ label: row.question, value, isSubAnswer: true });
      return;
    }

    currentGroup = null;
    rows.push({ label: row.question, value });
  });

  return rows;
};

// The pre-filled banner's body (BLUM-7856 AC4), naming the sources that actually contributed.
const PREFILL_SOURCE_LABEL: Record<MaturityAnswerSource, string> = {
  quarterly: 'your quarterly reports',
  prior_assessment: 'your previous maturity assessment',
  baseline_2025: 'your 2025 maturity assessment survey',
  user: '',
};

const joinNames = (names: string[]): string => (names.length <= 1 ? names.join('') : `${names.slice(0, -1).join(', ')} and ${names[names.length - 1]}`);

export const prefillBannerBody = (sources: MaturityAnswerSource[]): string => {
  const names = sources.map((source) => PREFILL_SOURCE_LABEL[source]).filter(Boolean);
  const from = names.length > 0 ? ` using information from ${joinNames(names)}` : '';
  return `We have pre-filled some questions${from}. Check each section and update any answers that have changed.`;
};

// "Manage your maturity assessment" success banners (location.state.banner + the section title).
export const MATURITY_TASKS_BANNER: Record<'sectionCompleted' | 'progressSaved', { title: string; body: (sectionTitle: string) => string }> = {
  sectionCompleted: { title: 'Section completed', body: (sectionTitle) => `‘${sectionTitle}’ is up to date.` },
  progressSaved: { title: 'Progress saved', body: (sectionTitle) => `You can come back to ‘${sectionTitle}’ and continue later.` },
};
