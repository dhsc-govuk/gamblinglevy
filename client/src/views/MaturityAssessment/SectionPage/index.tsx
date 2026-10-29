import React, { useEffect, useMemo, useRef, useState } from 'react';
import type { AxiosResponse } from 'axios';
import { Navigate, useNavigate, useParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { GeneralLayout } from '../../../layouts';
import { ErrorSummary, GovUKButton, GovUKInsetText, H1, PageLoading, SummaryList } from '../../../components/GovUKComponents';
import QuestionBlocks from '../../../components/Global/MaturityAssessment/QuestionBlocks';
import { useAuthProvider } from '../../../components/AuthProvider';
import usePageTitle from '../../../hooks/usePageTitle';
import { maturityContextKey, maturitySectionKey, maturityTaskListKey } from '../../../helpers/queriesParams';
import { ROUTES } from '../../../helpers/routes';
import { trackEvent } from '../../../helpers/analytics';
import { questionErrorTargetId, validateAnswers } from '../../Activities/answers';
import { getMaturitySection, type MaturityAnswerDto, type MaturitySectionDto } from '../queries';
import { saveMaturityPage } from '../mutations';
import { mapMaturityError } from '../messages';
import { maturityAnswersToRecord, maturityPeriodValue, toPageFormQuestion } from '../display';
import { nextPageCode, previousPageCode } from '../pages';
import { cleanRoles, emptyRole, parseRoles, pruneRoleErrors, roleFieldName, rolesErrorItems, serialiseRoles, validateRoles, type RoleEntry } from '../roles';
import { useMaturityContextFor } from '../context';
import { pruneHiddenAnswers, validateAnswerLengths } from '../answers';
import { canEditMaturityAssessment } from '../navigation';
import './styles.css';

const CONTINUE_ID = 'la-maturity-page-continue';

interface SectionPageProps {
  collectionId: string;
  sectionCode: string;
  pageCode: string;
}

// One page of a section (BLUM-7858, Figma 2744-182226+): the page's questions, Continue (validates this page, saves it
// and moves to the next visible page or the section check) and "Save and come back later" (a draft save back to the
// task list). Saving sends only this page's answers, so a pre-filled answer on a page the user never opened keeps
// its source (BLUM-7856 AC4).
const SectionPageContent = ({ collectionId, sectionCode, pageCode }: SectionPageProps): React.ReactElement => {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { userRole } = useAuthProvider();
  const canEdit = canEditMaturityAssessment(userRole);

  const [answers, setAnswers] = useState<Record<string, string>>({});
  const [roles, setRoles] = useState<RoleEntry[]>([emptyRole()]);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [submitAttempts, setSubmitAttempts] = useState(0);

  const { context, isLoading: contextLoading, isError: contextError } = useMaturityContextFor(collectionId);
  const tasksPath = ROUTES.reports.maturity(collectionId);
  const checkPath = ROUTES.reports.maturitySectionCheck(collectionId, sectionCode);

  const sectionQuery = useQuery({
    queryKey: maturitySectionKey(collectionId, sectionCode),
    queryFn: () => getMaturitySection(collectionId, sectionCode),
    enabled: !!collectionId && !!sectionCode && !!context?.assessmentId,
  });
  const section = sectionQuery.data?.data;
  const page = useMemo(() => section?.pages.find((candidate) => candidate.code === pageCode), [section, pageCode]);
  const rolesQuestion = useMemo(() => page?.questions.find((question) => question.questionType === 'Roles'), [page]);
  const rolesRoot = rolesQuestion?.code.toLowerCase() ?? '';
  const pageQuestions = useMemo(() => (page?.questions ?? []).filter((question) => question.questionType !== 'Roles').map(toPageFormQuestion), [page]);

  // Hydrate once per mount from the saved answers. The component is keyed by section/page (see the default export),
  // so moving between pages always remounts and re-hydrates.
  const hydratedRef = useRef(false);
  useEffect(() => {
    if (section && !hydratedRef.current) {
      hydratedRef.current = true;
      const saved = maturityAnswersToRecord(section.answers);
      setAnswers(saved);
      if (rolesQuestion) {
        const parsed = parseRoles(saved[rolesQuestion.code]);
        setRoles(parsed.length > 0 ? parsed : [emptyRole()]);
      }
    }
  }, [rolesQuestion, section]);

  const hasErrors = Object.keys(errors).length > 0;
  usePageTitle(`${hasErrors ? 'Error: ' : ''}${page?.title ?? 'Maturity assessment'}`);

  const saveMutation = useMutation({
    mutationFn: saveMaturityPage,
    onSuccess: (response, variables) => {
      // Seed the cached section from what was just saved BEFORE invalidating. react-query keeps the stale entry
      // through a refetch, and the next page mounts — and hydrates its one-shot local state — before the refetch
      // lands, so it would otherwise decide page visibility and Back/Continue from this page's OLD answer
      // (the BLUM-7686 invalidate-then-navigate race).
      queryClient.setQueryData<AxiosResponse<MaturitySectionDto> | undefined>(maturitySectionKey(collectionId, sectionCode), (cached) => {
        if (!cached) {
          return cached;
        }
        const byCode = new Map<string, MaturityAnswerDto>(cached.data.answers.map((answer) => [answer.questionCode, answer]));
        variables.answers.forEach((answer) => {
          if (answer.value) {
            byCode.set(answer.questionCode, { questionCode: answer.questionCode, value: answer.value, source: 'user' });
          } else {
            byCode.delete(answer.questionCode);
          }
        });
        return { ...cached, data: { ...cached.data, status: response.data.status, answers: [...byCode.values()] } };
      });
      queryClient.invalidateQueries({ queryKey: maturityContextKey(collectionId) });
      queryClient.invalidateQueries({ queryKey: maturityTaskListKey(collectionId) });
      queryClient.invalidateQueries({ queryKey: maturitySectionKey(collectionId, sectionCode) });
    },
  });

  if (contextError || sectionQuery.isError) {
    return <Navigate to={tasksPath} replace />;
  }

  if (!contextLoading && (!context || !context.assessmentId)) {
    return <Navigate to={tasksPath} replace />;
  }

  // Submitted, or a window that has closed since — read-only, so nothing here can be saved.
  if (context?.submittedAt) {
    return <Navigate to={ROUTES.reports.maturitySubmittedConfirmation(collectionId)} replace />;
  }

  if (section && !page) {
    return <Navigate to={ROUTES.reports.maturitySection(collectionId, sectionCode)} replace />;
  }

  const isLoading = contextLoading || sectionQuery.isLoading || !section || !page || !context;

  // Visibility of the pages around this one follows the merged answers: what was saved plus what has been typed here.
  const mergedAnswers = (): Record<string, string> => {
    const merged = { ...answers };
    if (rolesQuestion) {
      merged[rolesQuestion.code] = cleanRoles(roles).length > 0 ? serialiseRoles(rolesQuestion, roles) : '';
    }
    return merged;
  };

  const nextPath = (): string => {
    const next = section ? nextPageCode(section.pages, mergedAnswers(), pageCode) : undefined;
    return next ? ROUTES.reports.maturitySectionPage(collectionId, sectionCode, next) : checkPath;
  };

  const previous = section ? previousPageCode(section.pages, mergedAnswers(), pageCode) : undefined;
  const backHref = previous ? ROUTES.reports.maturitySectionPage(collectionId, sectionCode, previous) : tasksPath;

  const applyServerError = (error: unknown) => {
    const mapped = mapMaturityError(error);
    const fieldErrors = { ...mapped.fieldErrors };
    // A refusal of the repeater's JSON is worded "… for role N" server-side; land it on that row's control (the time
    // band or the other-role name when the message names them), else on the row's role radios.
    if (rolesQuestion && fieldErrors[rolesQuestion.code]) {
      const message = fieldErrors[rolesQuestion.code];
      const row = Math.max(0, Number(message.match(/for role (\d+)/)?.[1] ?? 1) - 1);
      const field = /time the funding/i.test(message) ? 'time' : /other role/i.test(message) ? 'other' : 'role';
      fieldErrors[roleFieldName(rolesRoot, row, field)] = message;
      delete fieldErrors[rolesQuestion.code];
    }
    setErrors(Object.keys(fieldErrors).length > 0 ? fieldErrors : { submit: mapped.message });
    setSubmitAttempts((attempts) => attempts + 1);
  };

  const save = (saveAsDraft: boolean, onSaved: () => void) => {
    const merged = mergedAnswers();
    const codes = [...pageQuestions.map((question) => question.code), ...(rolesQuestion ? [rolesQuestion.code] : [])];
    const shown = pruneHiddenAnswers(pageQuestions, merged);
    saveMutation.mutate(
      {
        collectionId,
        sectionCode,
        pageCode,
        saveAsDraft,
        // Every question on this page, cleared ones included — the backend replaces exactly these codes. A hidden one
        // (however deep its conditional chain) is sent blank: the backend deletes it either way, and a stale value there
        // could only get the request refused over the answer length limit (and be seeded into the cache as a user answer).
        answers: codes.map((questionCode) => ({ questionCode, value: shown[questionCode] ?? '' })),
      },
      { onSuccess: onSaved, onError: applyServerError }
    );
  };

  // Validated against what the page shows, not what local state still remembers: validateAnswers' visibility check is
  // one level deep, so without pruning a required field under a conditional parent that is itself now hidden would
  // demand an answer the user cannot see (see pruneHiddenAnswers).
  const validate = (enforceRequired: boolean): Record<string, string> => ({
    ...validateAnswers(pageQuestions, pruneHiddenAnswers(pageQuestions, answers), { enforceRequired }),
    ...validateAnswerLengths(pageQuestions, answers),
    ...(rolesQuestion ? validateRoles(rolesRoot, rolesQuestion, roles, { enforceRequired }) : {}),
  });

  const handleContinue = () => {
    if (!canEdit) {
      navigate(nextPath());
      return;
    }
    const validationErrors = validate(true);
    setErrors(validationErrors);
    setSubmitAttempts((attempts) => attempts + 1);
    if (Object.keys(validationErrors).length > 0) {
      return;
    }
    save(false, () => navigate(nextPath()));
  };

  const handleSaveDraft = () => {
    const validationErrors = validate(false);
    setErrors(validationErrors);
    setSubmitAttempts((attempts) => attempts + 1);
    if (Object.keys(validationErrors).length > 0) {
      return;
    }
    save(true, () => {
      trackEvent('maturity_draft_saved', { collectionId, section: sectionCode, page: pageCode });
      navigate(tasksPath, { state: { banner: 'progressSaved', sectionTitle: section?.title } });
    });
  };

  const handleAnswerChange = (questionCode: string, value: string) => {
    setAnswers((previousAnswers) => ({ ...previousAnswers, [questionCode]: value }));
  };

  // A row's change can take one of its controls off the page — "None" (BLUM-9112) unmounts the time question, leaving
  // "Other role" closes the name's reveal — or, on Remove, move the rows below up one. An error from an earlier Continue
  // that no longer describes what is on the page is dropped (pruneRoleErrors) rather than left in the summary.
  const handleRolesChange = (next: RoleEntry[], removedIndex?: number) => {
    setRoles(next);
    if (rolesQuestion) {
      setErrors((previous) => pruneRoleErrors(rolesQuestion, rolesRoot, previous, next, removedIndex));
    }
  };

  // GDS lists errors in the order the fields appear on the page. They are gathered by kind (required, length, server,
  // the roles repeater), so each is sorted back to its question's position; the sort is stable, so the repeater's rows
  // keep their own order. A key that is not a question (the submit banner) goes last.
  const pageOrder = (key: string): number => pageQuestions.find((question) => question.code === key)?.displayOrder ?? Number.MAX_SAFE_INTEGER;

  const errorList = [
    ...Object.entries(errors)
      .filter(([key, message]) => Boolean(message) && !(rolesRoot && key.startsWith(`${rolesRoot}-`)))
      .map(([key, message]) => {
        const question = pageQuestions.find((item) => item.code === key);
        return { order: pageOrder(key), item: { targetName: question ? questionErrorTargetId(question) : CONTINUE_ID, text: message } };
      }),
    ...(rolesQuestion ? rolesErrorItems(rolesRoot, rolesQuestion, errors).map((item) => ({ order: rolesQuestion.displayOrder, item })) : []),
  ]
    .sort((a, b) => a.order - b.order)
    .map(({ item }) => item);

  return (
    <GeneralLayout backLink={{ href: backHref, testId: 'la-maturity-page-back' }}>
      <div className="maturity-page" data-testid="la-maturity-page">
        {isLoading || !section || !page || !context ? (
          <PageLoading />
        ) : (
          <>
            <ErrorSummary heading="There is a problem" errors={errorList} focusTrigger={submitAttempts} testId="la-maturity-page-errors" />

            <div className="maturity-page__header">
              <span className="govuk-caption-l">{section.title}</span>
              <H1>{page.title}</H1>
              <GovUKInsetText className="maturity-page__details" data-testid="la-maturity-page-details">
                <SummaryList
                  halfWidthColumns
                  testId="la-maturity-page-summary"
                  items={[{ label: 'Reporting period', value: maturityPeriodValue(context.reportingPeriodStart, context.reportingPeriodEnd) }]}
                />
              </GovUKInsetText>
            </div>

            <div className="maturity-page__form">
              <QuestionBlocks
                page={page}
                answers={answers}
                errors={errors}
                onAnswerChange={handleAnswerChange}
                roles={roles}
                onRolesChange={handleRolesChange}
                testId="la-maturity-page-questions"
              />
            </div>

            <div className="govuk-button-group maturity-page__actions">
              <GovUKButton id={CONTINUE_ID} name={CONTINUE_ID} onClick={handleContinue} isLoading={saveMutation.isPending} data-testid="la-maturity-page-continue">
                Continue
              </GovUKButton>
              {canEdit && (
                <GovUKButton className="govuk-button govuk-button--secondary" onClick={handleSaveDraft} isLoading={saveMutation.isPending} data-testid="la-maturity-page-save">
                  Save and come back later
                </GovUKButton>
              )}
            </div>
          </>
        )}
      </div>
    </GeneralLayout>
  );
};

// Keyed by section and page so local state (answers, roles, errors) remounts when the URL moves to another page;
// without the key a Continue into the next page would carry the previous page's hydration guard with it.
const SectionPage = (): React.ReactElement => {
  const { collectionId = '', sectionCode = '', pageCode = '' } = useParams<{ collectionId: string; sectionCode: string; pageCode: string }>();
  return <SectionPageContent key={`${collectionId}/${sectionCode}/${pageCode}`} collectionId={collectionId} sectionCode={sectionCode} pageCode={pageCode} />;
};

export default SectionPage;
