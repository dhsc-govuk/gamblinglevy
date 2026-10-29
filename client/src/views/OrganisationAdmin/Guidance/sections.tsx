import { Link as RouterLink } from 'react-router-dom';
import { H3, H4, ListItem, OrderedList, Paragraph, UnorderedList } from '../../../components/GovUKComponents';
import { GuidanceSection, ReportingDeadlines, type DeadlineRow } from '../../../components/Global/Guidance';
import { ROUTES } from '../../../helpers/routes';
import { GUIDANCE_SUPPORT_EMAIL, GUIDANCE_SUPPORT_MAILTO, GUIDANCE_TEAMS_CHANNEL_NAME, GUIDANCE_TEAMS_CHANNEL_URL, STATUTORY_GAMBLING_LEVY_URL } from '../../../helpers/constants';

// LA guidance copy, transcribed from the approved draft "Private beta - LA guidance v7" attached to
// BLUM-7633 and brought up to the revision attached to BLUM-8591. Wording, list order and list style
// (bulleted vs numbered) follow the document exactly.
//
// Two known oddities are reproduced verbatim on purpose, and are raised with the content author rather than
// silently corrected here:
//   - the Q3 row opens "in early December 2027", where the other three rows open inside the quarter they
//     cover;
//   - the FY2026/2027 Maturity Assessment deadline is 30 April 2027 here and 30 April 2026 in the DHSC
//     draft, so each page ships its own document's date.
//
// The DHSC page keeps its own copy of this table at views/Admin/Guidance/sections.tsx, because the two
// audiences have two independently approved source documents. A timetable change therefore has to be
// applied to BOTH files — one of them going stale is invisible to the build, the linter and the type
// checker.
const deadlines: DeadlineRow[] = [
  { period: 'Quarter 1 (April to June 2026)', window: 'Opens 22 September 2026', deadline: '23 October 2026' },
  { period: 'Quarter 2 (July to September 2026)', window: 'Opens 22 September 2026', deadline: '23 October 2026' },
  { period: 'Quarter 3 (October to December 2026)', window: 'Opens in early December 2027', deadline: '22 January 2027' },
  { period: 'Quarter 4 (January to March 2027)', window: 'Opens in early March 2027', deadline: '23 April 2027' },
  { period: 'FY2026/2027 Maturity Assessment', window: 'To be confirmed', deadline: '30 April 2027' },
];

export const laGuidanceSections: GuidanceSection[] = [
  {
    id: 'about-the-service',
    title: 'About the service',
    content: (
      <>
        <Paragraph>
          Share progress preventing gambling harms is a digital service for local authorities who receive funding under the{' '}
          <a className="govuk-link" href={STATUTORY_GAMBLING_LEVY_URL} data-testid="organisation-admin-guidance-link-levy-publication">
            statutory gambling levy
          </a>
          .
        </Paragraph>
        <Paragraph>
          It has been set up with the Department of Health and Social Care (DHSC) to collect data on the ways local authorities are working to prevent gambling harms in their area.
        </Paragraph>
        <Paragraph>It has been designed to help local authorities:</Paragraph>
        <UnorderedList>
          <ListItem>monitor their progress</ListItem>
          <ListItem>accurately report on their levy spending and forecasted spend each quarter</ListItem>
        </UnorderedList>
        <Paragraph>It will also help DHSC and local authorities share knowledge and learn which activities work to reduce or prevent gambling harms.</Paragraph>

        <H3>How the information will be used</H3>
        <Paragraph>By providing this information, you will help DHSC:</Paragraph>
        <UnorderedList>
          <ListItem>understand what activities are being delivered and the progress local authorities are making to prevent gambling harms</ListItem>
          <ListItem>build an evidence base about what approaches may be effective and where additional support may be needed</ListItem>
          <ListItem>monitor how statutory gambling levy funding is being spent, including actual and forecasted expenditure</ListItem>
          <ListItem>support grant assurance and reporting requirements</ListItem>
          <ListItem>support annual maturity assessments and future funding decisions</ListItem>
        </UnorderedList>
        <Paragraph>
          Reporting on progress does not mean that you must demonstrate immediate changes in gambling harms outcomes. The measures you report should be appropriate to the activity
          and proportionate to the stage of delivery.
        </Paragraph>

        <H3>Who can see the information you submit</H3>
        <Paragraph>The information you submit through your quarterly reports will only be available to authorised users within your local authority and DHSC.</Paragraph>
      </>
    ),
  },
  {
    id: 'adding-users',
    title: 'Adding users',
    content: (
      <>
        <Paragraph>
          You can have more than one user in your local authority. This is useful if you will have more than one person adding information or completing reports.
        </Paragraph>
        <Paragraph>Each user will need to have their own authorised account.</Paragraph>
        <Paragraph>
          LA Admin users can{' '}
          <RouterLink to={ROUTES.organisationAdmin.users.list} className="govuk-link" data-testid="organisation-admin-guidance-link-users">
            manage user accounts and access
          </RouterLink>{' '}
          from the homepage.
        </Paragraph>
        <Paragraph>
          If you need help adding users, contact your local authority’s gambling harms prevention lead, your DHSC regional lead, or email{' '}
          <a className="govuk-link" href={GUIDANCE_SUPPORT_MAILTO} data-testid="organisation-admin-guidance-link-email-adding-users">
            {GUIDANCE_SUPPORT_EMAIL}
          </a>
          .
        </Paragraph>
      </>
    ),
  },
  {
    id: 'adding-activities',
    title: 'Adding activities',
    content: (
      <>
        <Paragraph>
          To add your gambling harms prevention activities,{' '}
          <RouterLink to={ROUTES.activities.list} className="govuk-link" data-testid="organisation-admin-guidance-link-activities">
            manage your list of activities
          </RouterLink>
          . You do not need to enter financial information when you add activities.
        </Paragraph>
        <Paragraph>You can:</Paragraph>
        <UnorderedList>
          <ListItem>add activities to your account at any time - you do not need to wait for a reporting period</ListItem>
          <ListItem>save your progress and edit your drafts at a later date - you do not have to add them all in one go</ListItem>
        </UnorderedList>
        <Paragraph>
          The information from these activities will be pulled into your quarterly reports and maturity assessment when it’s time to report. You will have the chance to review the
          information first.
        </Paragraph>
        <Paragraph>
          You do not need to create the same activity each quarter. You can create an activity once, then update it as it progresses. The activity will appear in future reporting
          periods.
        </Paragraph>

        <H3>Types of activities to report on</H3>
        <Paragraph>
          You should tell us about any gambling harms prevention activities funded or partially funded by the statutory gambling levy. This is so DHSC can see where funding is
          being spent.
        </Paragraph>
        <Paragraph>These can be activities you have planned, that are already in progress or that you have closed or finished.</Paragraph>
        <Paragraph>Activities may include things like:</Paragraph>
        <UnorderedList>
          <ListItem>creating a needs assessment</ListItem>
          <ListItem>hiring people to focus on gambling harms prevention</ListItem>
          <ListItem>developing your strategy and leadership</ListItem>
          <ListItem>building gambling harms prevention into your licensing, planning and policy</ListItem>
          <ListItem>promoting awareness through education, campaigns and outreach</ListItem>
          <ListItem>working with partnership organisations</ListItem>
          <ListItem>providing help, advice and access to services</ListItem>
          <ListItem>contributing funds to pool of funding for gambling harms prevention work</ListItem>
        </UnorderedList>
        <Paragraph>The information we’ll ask for will vary by activity. It may include:</Paragraph>
        <UnorderedList>
          <ListItem>who you will work with</ListItem>
          <ListItem>what you are doing or plan to do</ListItem>
          <ListItem>whether it’s an activity you’re sharing with other local authorities</ListItem>
          <ListItem>your expected results</ListItem>
          <ListItem>when the work started</ListItem>
        </UnorderedList>
        <Paragraph>If part of a wider project is funded through the levy, record only the levy-funded gambling harms prevention activity.</Paragraph>
        <Paragraph>Do not record:</Paragraph>
        <UnorderedList>
          <ListItem>activities that are unrelated to preventing gambling harms</ListItem>
          <ListItem>work that receives no statutory gambling levy funding or support</ListItem>
          <ListItem>routine business-as-usual activity with no gambling harms prevention element</ListItem>
        </UnorderedList>

        <H4>Joint partnership activities</H4>
        <Paragraph>
          You’ll need to tell us if you are contributing to a joint partnership with other local authorities. You should do this if you’re contributing any amount of your gambling
          levy allocation to a pooled fund meant for gambling harms prevention activities. This includes regional partnerships. You’ll need to tell us how much of your levy
          allocation you have committed to the partnership, and which other local authorities are part of the partnership.
        </Paragraph>

        <H3>Activity states</H3>
        <Paragraph>The different activity states are as follows:</Paragraph>
        <UnorderedList>
          <ListItem>draft - activities you have not finished creating or want to edit later</ListItem>
          <ListItem>planned - activities that are approved but not started</ListItem>
          <ListItem>active - activities you are currently working on</ListItem>
          <ListItem>closed - activities that were planned or in progress but you decided not to continue</ListItem>
          <ListItem>finished - work or projects that are complete, or positions that have been filled</ListItem>
        </UnorderedList>

        <H3>When to change an activity’s status</H3>
        <Paragraph>You should:</Paragraph>
        <UnorderedList>
          <ListItem>change ‘planned’ activities to ‘active’ when work begins</ListItem>
          <ListItem>change ‘active’ activities to ‘finished’ when work has been completed</ListItem>
          <ListItem>change ‘planned’ or ‘active’ activities to ‘closed’ if the activity stops before completion</ListItem>
        </UnorderedList>
      </>
    ),
  },
  {
    id: 'editing-activities',
    title: 'Editing activities',
    content: (
      <>
        <Paragraph>You might need to edit an activity if plans change or to add more information, or if you made a mistake.</Paragraph>
        <Paragraph>You can go back and edit the following activities at any time:</Paragraph>
        <UnorderedList>
          <ListItem>drafts</ListItem>
          <ListItem>planned</ListItem>
          <ListItem>active</ListItem>
        </UnorderedList>
        <Paragraph>This includes the activity details and your expected results.</Paragraph>
        <Paragraph>Make sure the information in each ‘active’ activity is up to date before submitting your quarterly report.</Paragraph>

        <H3>Closed and finished activities</H3>
        <Paragraph>
          Closed and finished activities form part of your reporting record. They cannot be changed after they have been checked and saved. This is so reporting remains accurate.
          We recommend you save these as drafts until you are confident they are correct.
        </Paragraph>
        <Paragraph>
          If you need to change the details of a closed or finished activity, contact your DHSC regional lead or email{' '}
          <a className="govuk-link" href={GUIDANCE_SUPPORT_MAILTO} data-testid="organisation-admin-guidance-link-email-editing-activities">
            {GUIDANCE_SUPPORT_EMAIL}
          </a>
          .
        </Paragraph>
      </>
    ),
  },
  {
    id: 'deleting-activities',
    title: 'Deleting activities',
    content: (
      <>
        <Paragraph>You should only delete activities that have been created in error, for example if you create the same activity twice.</Paragraph>
        <Paragraph>If an activity has finished, you should update the activity status to ‘finished’.</Paragraph>
        <Paragraph>If you have decided not to continue with an activity that has already started, you should update the activity status to ‘closed’.</Paragraph>
        <Paragraph>If an activity is still a draft or listed as a planned activity and it has been duplicated by accident, you can delete it.</Paragraph>
        <Paragraph>
          If you need to delete an activity that is saved as active, finished, or closed, email{' '}
          <a className="govuk-link" href={GUIDANCE_SUPPORT_MAILTO} data-testid="organisation-admin-guidance-link-email-deleting-activities">
            {GUIDANCE_SUPPORT_EMAIL}
          </a>
          .
        </Paragraph>
      </>
    ),
  },
  {
    id: 'reporting-deadlines',
    title: 'Reporting deadlines',
    content: (
      <>
        <Paragraph>The first year of reporting has a phased timetable to support you as you begin using the service:</Paragraph>
        <ReportingDeadlines rows={deadlines} testId="organisation-admin-guidance-deadlines-table" />

        <H3>Quarterly reports</H3>
        <Paragraph>Starting in financial year 2027/2028, quarterly reporting will follow the same pattern each financial year:</Paragraph>
        <UnorderedList>
          <ListItem>reporting windows will open during the final month of each quarter</ListItem>
          <ListItem>the submission deadline will always be 3 weeks after the end of that quarter</ListItem>
        </UnorderedList>
        <Paragraph>DHSC will:</Paragraph>
        <UnorderedList>
          <ListItem>send you an email when it’s time to create a report</ListItem>
          <ListItem>tell you the deadline for submitting data</ListItem>
        </UnorderedList>

        <H3>Maturity assessment</H3>
        <Paragraph>The annual maturity assessment helps you and DHSC review your local authority’s progress in preventing gambling harms.</Paragraph>
        <Paragraph>When the reporting window opens, your maturity assessment will already contain:</Paragraph>
        <UnorderedList>
          <ListItem>the responses your local authority submitted in the maturity assessment the previous year</ListItem>
          <ListItem>relevant information pulled directly from your quarterly reports</ListItem>
        </UnorderedList>
        <Paragraph>
          You should review the pre-filled information, update it where necessary and complete any remaining questions before submitting your maturity assessment at the end of the
          financial year.
        </Paragraph>
        <Paragraph>DHSC will email you when the maturity assessment opens and confirm the submission deadline.</Paragraph>
        <Paragraph>The first payment for Quarter 1 in financial year 2027/2028 will depend on the submission of your maturity assessment.</Paragraph>
      </>
    ),
  },
  {
    id: 'quarterly-reporting',
    title: 'Quarterly reporting',
    content: (
      <>
        <H3>Deadlines</H3>
        <Paragraph>
          For reporting deadlines, read the guidance on{' '}
          <a className="govuk-link" href="#reporting-deadlines" data-testid="organisation-admin-guidance-link-reporting-deadlines">
            reporting deadlines
          </a>
          .
        </Paragraph>

        <H3>Notifications</H3>
        <Paragraph>We will email you when a reporting window is open. The email will confirm the deadline for submitting your report.</Paragraph>

        <H3>Completing your quarterly report</H3>
        <Paragraph>You can only submit one report for each reporting period.</Paragraph>
        <Paragraph>You can review your report before submitting it.</Paragraph>
        <Paragraph>There are a few stages to completing a quarterly report. Read the guidance on this page to find out how to:</Paragraph>
        <OrderedList>
          <ListItem>Confirm your activities.</ListItem>
          <ListItem>Enter progress against your measures.</ListItem>
          <ListItem>Record your levy spend.</ListItem>
          <ListItem>Provide strategic feedback.</ListItem>
        </OrderedList>

        <H3>Confirming your activities</H3>
        <Paragraph>
          Before you can start your report, you will need to add activities. To do this, read the guidance on{' '}
          <a className="govuk-link" href="#adding-activities" data-testid="organisation-admin-guidance-link-adding-activities">
            adding activities
          </a>
          .
        </Paragraph>
        <Paragraph>The activities you create will be pulled into your quarterly report if they are either:</Paragraph>
        <UnorderedList>
          <ListItem>active</ListItem>
          <ListItem>finished</ListItem>
          <ListItem>closed</ListItem>
        </UnorderedList>
        <Paragraph>You do not need to recreate an activity each quarter. You only need to create the activity once then provide an update during your quarterly report.</Paragraph>
        <Paragraph>
          When you start your report, you will need to confirm your activities are up to date. You’ll be able to edit or update any planned or active activities at this stage. All
          active, finished, and closed activities will be pulled through into your quarterly report.
        </Paragraph>
        <Paragraph>Draft and planned activities are not included in quarterly reports.</Paragraph>

        <H3>Reporting your progress</H3>
        <Paragraph>
          For each activity, you will be asked to describe your progress. This will be your progress against the measures you selected when you created the activity. These measures
          vary by activity.
        </Paragraph>

        <H3>Recording levy spend</H3>
        <Paragraph>For each activity in your quarterly report, you will be asked to enter:</Paragraph>
        <OrderedList>
          <ListItem>Your actual levy spend for the quarter.</ListItem>
          <ListItem>Your forecasted levy spend to the end of the financial year.</ListItem>
        </OrderedList>
        <Paragraph>You should enter the spend to the nearest pound, like £5,000 or £12,345. Do not include pence.</Paragraph>
        <Paragraph>Do not include funding from other sources. If an activity is partially funded by another source, enter only the amount covered by the levy.</Paragraph>

        <H4>Actual spend</H4>
        <Paragraph>Actual spend should include:</Paragraph>
        <UnorderedList>
          <ListItem>levy expenditure that relates to work completed during the reporting period</ListItem>
          <ListItem>accrued expenditure</ListItem>
        </UnorderedList>
        <Paragraph>
          If you completed the activity or expenditure before the end of the financial year, but have not received or processed the invoice, you can accrue the expenditure.
        </Paragraph>
        <Paragraph>
          For example, if work was completed by 31 March but you did not receive or process the supplier’s invoice by that date, the expenditure can be included as an accrual.
        </Paragraph>
        <Paragraph>
          Accrued expenditure should be included as actual spend for the relevant reporting period. You should keep a record of the accrual in line with your local authority’s
          normal financial procedures.
        </Paragraph>
        <Paragraph>Do not enter committed or forecasted spend as actual spend. You will be asked separately for your forecasted spend.</Paragraph>

        <H4>Forecasted spend</H4>
        <Paragraph>You will be asked to report on forecasted spend for each activity in your quarterly report for the remaining quarters in the current financial year.</Paragraph>
        <Paragraph>Forecasted or committed spend is money that your local authority has agreed or intends to spend, but where the expenditure has not yet been incurred.</Paragraph>
        <Paragraph>For example, a purchase order has been raised for work that will take place in a future reporting period.</Paragraph>
        <Paragraph>Do not include actual spend as forecasted spend.</Paragraph>

        <H3>Providing strategic feedback</H3>
        <Paragraph>In the final part of the quarterly report, you can tell us about:</Paragraph>
        <UnorderedList>
          <ListItem>any gambling harms prevention activities not funded by the levy</ListItem>
          <ListItem>any challenges you have faced</ListItem>
          <ListItem>any feedback you have for DHSC</ListItem>
          <ListItem>whether you have any learnings to share with other local authorities</ListItem>
        </UnorderedList>
        <Paragraph>Some of these questions are optional.</Paragraph>
        <Paragraph>If you tell us you have learnings to share with other local authorities, DHSC will contact you after you have submitted your report.</Paragraph>

        <H3>Submitting your report</H3>
        <Paragraph>You will be able to review your answers before you submit your report. When you are ready, select ‘Submit.’ This will send the report to DHSC.</Paragraph>
        <Paragraph>
          You will not be able to edit your report once you have submitted it. If you have made a mistake, email{' '}
          <a className="govuk-link" href={GUIDANCE_SUPPORT_MAILTO} data-testid="organisation-admin-guidance-link-email-submitting">
            {GUIDANCE_SUPPORT_EMAIL}
          </a>
          .
        </Paragraph>
        <Paragraph>Once your report has been submitted, you can view and download it for your records.</Paragraph>
        <Paragraph>DHSC will review your report alongside those submitted by other local authorities. If they have any questions, they will contact you.</Paragraph>

        <H3>Downloading your report</H3>
        <Paragraph>
          You can view{' '}
          <RouterLink to={ROUTES.reports.submitted} className="govuk-link" data-testid="organisation-admin-guidance-link-submitted-reports">
            submitted reports
          </RouterLink>{' '}
          from the homepage. From there, you can select a report to:
        </Paragraph>
        <UnorderedList>
          <ListItem>see who submitted it and when</ListItem>
          <ListItem>download a copy</ListItem>
        </UnorderedList>
        <Paragraph>If you download a report it will include the following spreadsheets:</Paragraph>
        <OrderedList>
          <ListItem>
            Report summary, showing high level information like:
            <UnorderedList>
              <ListItem>reporting period</ListItem>
              <ListItem>deadline</ListItem>
            </UnorderedList>
          </ListItem>
          <ListItem>
            Activity results, showing the results for each activity including your:
            <UnorderedList>
              <ListItem>progress against the measures you set</ListItem>
              <ListItem>challenges</ListItem>
              <ListItem>plans for the next quarter</ListItem>
            </UnorderedList>
          </ListItem>
          <ListItem>
            Funding spend, showing your:
            <UnorderedList>
              <ListItem>actual and forecasted spend for each activity</ListItem>
              <ListItem>funding allocation</ListItem>
              <ListItem>total spend for the year so far</ListItem>
              <ListItem>remaining allocation</ListItem>
            </UnorderedList>
          </ListItem>
          <ListItem>
            Strategic feedback, like:
            <UnorderedList>
              <ListItem>other gambling harms prevention work not funded by the levy</ListItem>
              <ListItem>challenges</ListItem>
              <ListItem>feedback for DHSC</ListItem>
              <ListItem>whether you have said you have learnings to share with others</ListItem>
            </UnorderedList>
          </ListItem>
        </OrderedList>
      </>
    ),
  },
  {
    id: 'if-you-miss-a-reporting-deadline',
    title: 'If you miss a reporting deadline',
    content: (
      <>
        <Paragraph>
          Email{' '}
          <a className="govuk-link" href={GUIDANCE_SUPPORT_MAILTO} data-testid="organisation-admin-guidance-link-email-missed-deadline">
            {GUIDANCE_SUPPORT_EMAIL}
          </a>{' '}
          as soon as possible.
        </Paragraph>
        <Paragraph>DHSC will tell you whether the reporting window can be reopened or whether alternative arrangements are available.</Paragraph>
      </>
    ),
  },
  {
    id: 'if-you-need-help-using-the-service',
    title: 'If you need help using the service',
    content: (
      <>
        <Paragraph>
          You can join us on Microsoft Teams at{' '}
          <a className="govuk-link" href={GUIDANCE_TEAMS_CHANNEL_URL} data-testid="organisation-admin-guidance-link-teams">
            {GUIDANCE_TEAMS_CHANNEL_NAME}
          </a>{' '}
          where we will be sharing resources and answering questions from users.
        </Paragraph>
        <Paragraph>The Microsoft Teams channel includes a troubleshooting guide for any technical issues.</Paragraph>
        <Paragraph>
          For technical problems or urgent questions about using the service, email{' '}
          <a className="govuk-link" href={GUIDANCE_SUPPORT_MAILTO} data-testid="organisation-admin-guidance-link-email-support">
            {GUIDANCE_SUPPORT_EMAIL}
          </a>
          .
        </Paragraph>
        <Paragraph>For questions about gambling harms prevention policy or funding, contact your DHSC regional lead.</Paragraph>
      </>
    ),
  },
];
