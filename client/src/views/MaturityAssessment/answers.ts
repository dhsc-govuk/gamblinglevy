import { isQuestionVisible } from '../../components/GovUKComponents';
import type { FormQuestionDto } from '../../types/formSchema';

// Mirrors FieldLimits.AnswerText, which SaveMaturityAnswersCommandValidator applies to every answer on a draft save and
// Continue alike. Maturity free text has no protocol character limit (BLUM-8411), so no character count is shown and
// validateAnswers has no maxLength to check. Without this, an over-long answer reaches the API and comes back as a 400
// that mapReportError can only report as a generic failure, with nothing on the field.
export const MATURITY_ANSWER_MAX_LENGTH = 10000;

// Mirrors MaturityProtocol.OptionalSuffix: the GDS marker belongs on the field's label, not inside an error message.
const OPTIONAL_SUFFIX = ' (optional)';

// The answers as the page actually shows them: every hidden question blanked, repeated until nothing changes.
// isQuestionVisible looks only at a question's direct parent, but conditionals nest (MA_ADV_EXEMPT hangs off
// MA_ADV_ALL_PROPERTY, which hangs off MA_ADV_RESTRICTED), so a grandchild of a parent the user has since changed still
// reads as visible until its own parent is blanked. The backend's visibility check is one level too, so sending these
// pruned values is also what makes it agree with the page. Only the page's own questions are pruned; anything else
// (the roles repeater, other pages' answers) passes through untouched.
export const pruneHiddenAnswers = (questions: FormQuestionDto[], answers: Record<string, string>): Record<string, string> => {
  const pruned = { ...answers };
  let changed = true;
  while (changed) {
    changed = false;
    questions.forEach((question) => {
      if (pruned[question.code] && !isQuestionVisible(question, pruned)) {
        pruned[question.code] = '';
        changed = true;
      }
    });
  }
  return pruned;
};

// Measured against the pruned answers, so a hidden question never gets an error the user can neither see nor reach —
// SectionPage sends it blank. The raw value is measured, not the trimmed one, because the API's MaximumLength counts
// the value as sent — don't "fix" this to .trim().
export const validateAnswerLengths = (questions: FormQuestionDto[], answers: Record<string, string>): Record<string, string> => {
  const shown = pruneHiddenAnswers(questions, answers);
  return questions.reduce<Record<string, string>>((errors, question) => {
    if ((shown[question.code] ?? '').length > MATURITY_ANSWER_MAX_LENGTH) {
      const label = question.label.endsWith(OPTIONAL_SUFFIX) ? question.label.slice(0, -OPTIONAL_SUFFIX.length) : question.label;
      errors[question.code] = `“${label}” must be ${MATURITY_ANSWER_MAX_LENGTH.toLocaleString('en-GB')} characters or less`;
    }
    return errors;
  }, {});
};
