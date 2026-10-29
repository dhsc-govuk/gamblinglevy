import { Link as RouterLink } from 'react-router-dom';
import { H3, H4, ListItem, OrderedList, Paragraph, UnorderedList } from '../../../components/GovUKComponents';
import { GuidanceSection, ReportingDeadlines, type DeadlineRow } from '../../../components/Global/Guidance';
import { ROUTES } from '../../../helpers/routes';
import { GUIDANCE_SUPPORT_EMAIL, GUIDANCE_SUPPORT_MAILTO, GUIDANCE_TEAMS_CHANNEL_NAME, GUIDANCE_TEAMS_CHANNEL_URL, STATUTORY_GAMBLING_LEVY_URL } from '../../../helpers/constants';

// DHSC guidance copy, transcribed from the approved draft "Private beta - DHSC user guidance v1" attached to
// BLUM-7633. Wording, list order and list style (bulleted vs numbered) follow the document exactly.
//
// Two known oddities are reproduced verbatim on purpose, and are raised with the content author rather than
// silently corrected here:
//   - the Q3 row opens "in early December 2027", where the other three rows open inside the quarter they
//     cover;
//   - the FY2026/2027 Maturity Assessment deadline is 30 April 2026 here and 30 April 2027 in the LA draft,
//     so each page ships its own document's date.
//
// The LA page keeps its own copy of this table at views/OrganisationAdmin/Guidance/sections.tsx, because the two audiences
// have two independently approved source documents. A timetable change therefore has to be applied to
// BOTH files — one of them going stale is invisible to the build, the linter and the type checker.
const deadlines: DeadlineRow[] = [
  { period: 'Quarter 1 (April to June 2026)', window: 'Opens 22 September 2026', deadline: '23 October 2026' },
  { period: 'Quarter 2 (July to September 2026)', window: 'Opens 22 September 2026', deadline: '23 October 2026' },
  { period: 'Quarter 3 (October to December 2026)', window: 'Opens in early December 2027', deadline: '22 January 2027' },
  { period: 'Quarter 4 (January to March 2027)', window: 'Opens in early March 2027', deadline: '23 April 2027' },
  { period: 'FY2026/2027 Maturity Assessment', window: 'To be confirmed', deadline: '30 April 2026' },
];

export const dhscGuidanceSections: GuidanceSection[] = [
  {
    id: 'about-the-service',
    title: 'About the service',
    content: (
      <>
        <Paragraph>
          Share progress preventing gambling harms is a digital service for organisations who receive funding under the{' '}
          <a className="govuk-link" href={STATUTORY_GAMBLING_LEVY_URL} data-testid="admin-guidance-link-levy-publication">
            statutory gambling levy
          </a>
          .
        </Paragraph>
        <Paragraph>
          It has been set up with the Department of Health and Social Care (DHSC) to collect data on the ways organisations are working to prevent gambling harms in their area.
        </Paragraph>
        <Paragraph>It has been designed to help organisations:</Paragraph>
        <UnorderedList>
          <ListItem>monitor their progress</ListItem>
          <ListItem>accurately report on their levy spending and forecasted spend each quarter</ListItem>
        </UnorderedList>
        <Paragraph>It will also help DHSC and organisations share knowledge and learn which activities work to reduce or prevent gambling harms.</Paragraph>

        <H3>Information captured by the service</H3>
        <Paragraph>
          We are asking organisations to tell us about any gambling harms prevention activities funded or partially funded by the statutory gambling levy. This is so we can see
          where funding is being spent.
        </Paragraph>
        <Paragraph>These can be activities that are planned, are already in progress or ones that have closed or finished.</Paragraph>
        <Paragraph>If organisations contribute funding or resources to a joint project, they can record this as a shared activity.</Paragraph>
        <Paragraph>Activities may include things like:</Paragraph>
        <UnorderedList>
          <ListItem>creating a needs assessment</ListItem>
          <ListItem>hiring people to focus on gambling harms prevention</ListItem>
          <ListItem>developing your strategy and leadership</ListItem>
          <ListItem>building gambling harms prevention into their licensing, planning and policy</ListItem>
          <ListItem>promoting awareness through education, campaigns and outreach</ListItem>
          <ListItem>working with partnership organisations</ListItem>
          <ListItem>providing help, advice and access to services</ListItem>
        </UnorderedList>
        <Paragraph>The information we’ll ask for will vary by activity. It may include:</Paragraph>
        <UnorderedList>
          <ListItem>who they will work with</ListItem>
          <ListItem>what they are doing or plan to do</ListItem>
          <ListItem>whether it’s an activity they are sharing with other organisations</ListItem>
          <ListItem>their expected results</ListItem>
          <ListItem>when the work started</ListItem>
        </UnorderedList>
        <Paragraph>If part of a wider project is funded through the levy, organisations should record only the levy-funded gambling harms prevention activity.</Paragraph>
        <Paragraph>Organisations should not record:</Paragraph>
        <UnorderedList>
          <ListItem>activities that are unrelated to preventing gambling harms</ListItem>
          <ListItem>work that receives no statutory gambling levy funding or support</ListItem>
          <ListItem>routine business-as-usual activity with no gambling harms prevention element</ListItem>
        </UnorderedList>

        <H3>Activities shared with other organisations</H3>
        <Paragraph>If an organisation contributes funding, staff time or other resources to shared work, they should record the activity as a shared activity.</Paragraph>
        <Paragraph>They should do this even if another organisation is leading on the work.</Paragraph>

        <H3>Who can see the information submitted by organisations</H3>
        <Paragraph>The information submitted will only be available to authorised users within each organisation and DHSC.</Paragraph>

        <H3>How the information will be used</H3>
        <Paragraph>By providing this information, local authorities will help DHSC:</Paragraph>
        <UnorderedList>
          <ListItem>understand what activities are being delivered and the progress local authorities are making to prevent gambling harms</ListItem>
          <ListItem>build an evidence base about what approaches may be effective and where additional support may be needed</ListItem>
          <ListItem>monitor how statutory gambling levy funding is being spent, including actual and forecasted expenditure</ListItem>
          <ListItem>support grant assurance and reporting requirements</ListItem>
          <ListItem>support annual maturity assessments and future funding decisions</ListItem>
        </UnorderedList>
        <Paragraph>
          Reporting on progress does not mean that local authorities must demonstrate immediate changes in gambling harms outcomes. The measures they report should be appropriate
          to the activity and proportionate to the stage of delivery.
        </Paragraph>
      </>
    ),
  },
  {
    id: 'adding-users',
    title: 'Adding users',
    content: (
      <>
        <Paragraph>You can have more than one user in your team. This is useful if you will have more than one person reviewing information or tracking submissions.</Paragraph>
        <Paragraph>Each user will need to have their own authorised account.</Paragraph>
        <Paragraph>
          DHSC Super Admins can{' '}
          <RouterLink to={ROUTES.admin.users.list} className="govuk-link" data-testid="admin-guidance-link-users">
            manage user accounts and access
          </RouterLink>{' '}
          from the homepage, both for:
        </Paragraph>
        <UnorderedList>
          <ListItem>DHSC users</ListItem>
          <ListItem>organisation users, like local authorities</ListItem>
        </UnorderedList>
        <Paragraph>
          If you need help adding users, email{' '}
          <a className="govuk-link" href={GUIDANCE_SUPPORT_MAILTO} data-testid="admin-guidance-link-email-adding-users">
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
        <Paragraph>When a reporting window opens, DHSC will:</Paragraph>
        <UnorderedList>
          <ListItem>send an email inviting organisations to submit their report</ListItem>
          <ListItem>send an email to DHSC users so they can track submissions</ListItem>
        </UnorderedList>
        <Paragraph>This email will include the reporting deadline.</Paragraph>
        <Paragraph>The first year of reporting has a phased timetable:</Paragraph>
        <ReportingDeadlines rows={deadlines} testId="admin-guidance-deadlines-table" />

        <H3>Deadlines for quarterly reports</H3>
        <Paragraph>Starting in FY2027/2028, quarterly reporting will follow the same pattern each financial year:</Paragraph>
        <UnorderedList>
          <ListItem>reporting windows will open during the final month of each quarter</ListItem>
          <ListItem>the submission deadline will always be 3 weeks after the end of that quarter</ListItem>
        </UnorderedList>

        <H3>Deadlines for maturity assessments</H3>
        <Paragraph>The annual maturity assessment helps both DHSC and organisations review progress in preventing gambling harms.</Paragraph>
        <Paragraph>When the reporting window opens, local authorities’ maturity assessments will already contain:</Paragraph>
        <UnorderedList>
          <ListItem>the responses their organisation submitted in the maturity assessment the previous year</ListItem>
          <ListItem>relevant information pulled directly from their quarterly reports</ListItem>
        </UnorderedList>
        <Paragraph>They will need to:</Paragraph>
        <OrderedList>
          <ListItem>Review the pre-filled information.</ListItem>
          <ListItem>Update it where necessary.</ListItem>
          <ListItem>Complete any remaining questions.</ListItem>
          <ListItem>Submit their maturity assessment at the end of the financial year.</ListItem>
        </OrderedList>
        <Paragraph>The first payment for Q1 in FY27/28 will depend on the submission of local authorities’ maturity assessments.</Paragraph>
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
          <a className="govuk-link" href="#reporting-deadlines" data-testid="admin-guidance-link-reporting-deadlines">
            reporting deadlines
          </a>
          .
        </Paragraph>

        <H3>Notifications</H3>
        <Paragraph>DHSC will email organisations when a new report is due. We will also send them reminders to complete their report.</Paragraph>
        <Paragraph>We will email you:</Paragraph>
        <UnorderedList>
          <ListItem>when a reporting window has opened, so you can track submissions</ListItem>
          <ListItem>when the reporting window closes</ListItem>
        </UnorderedList>

        <H3>Tracking submissions and downloading data</H3>
        <Paragraph>
          You can{' '}
          <RouterLink to={ROUTES.admin.reports.list} className="govuk-link" data-testid="admin-guidance-link-reports">
            review submitted reports
          </RouterLink>{' '}
          from the homepage. From there, you can select a report to:
        </Paragraph>
        <UnorderedList>
          <ListItem>see which organisations were included in the data collection</ListItem>
          <ListItem>see which organisations have submitted their data</ListItem>
          <ListItem>download consolidated or individual organisations’ reports</ListItem>
        </UnorderedList>

        <H4>Consolidated reports</H4>
        <Paragraph>When an organisation has submitted their report, it will be added to a consolidated report. You can download this.</Paragraph>
        <Paragraph>
          You do not need to wait until the reporting window has closed to download a report, however the report will update as organisations submit their data. The final version
          will be available when the reporting window has closed.
        </Paragraph>
        <Paragraph>When you download a consolidated report it will include the following spreadsheets:</Paragraph>
        <OrderedList>
          <ListItem>
            Summary, listing each organisation and their:
            <UnorderedList>
              <ListItem>submission date</ListItem>
              <ListItem>total levy spend for the reporting period</ListItem>
            </UnorderedList>
          </ListItem>
          <ListItem>
            Funding summary, listing each organisation and their:
            <UnorderedList>
              <ListItem>annual allocation</ListItem>
              <ListItem>actual quarterly spend to date</ListItem>
              <ListItem>forecasted spend for each quarter</ListItem>
              <ListItem>total forecasted spend for the year</ListItem>
              <ListItem>remaining allocation</ListItem>
              <ListItem>projected variance, which indicates whether they are likely to have overspend or underspend</ListItem>
            </UnorderedList>
          </ListItem>
          <ListItem>
            Funding spend, listing every activity for each organisation and the:
            <UnorderedList>
              <ListItem>actual spend for the activity</ListItem>
              <ListItem>forecasted spend for the activity for future quarters</ListItem>
            </UnorderedList>
          </ListItem>
          <ListItem>
            Activity results, listing every activity for each organisation and their:
            <UnorderedList>
              <ListItem>progress against the measures they set for each activity</ListItem>
              <ListItem>challenges</ListItem>
              <ListItem>plans for the next quarter</ListItem>
            </UnorderedList>
          </ListItem>
          <ListItem>
            Strategic feedback, listing each organisation and:
            <UnorderedList>
              <ListItem>their additional gambling harms prevention work not funded by the levy</ListItem>
              <ListItem>their challenges</ListItem>
              <ListItem>their feedback for DHSC</ListItem>
              <ListItem>whether the organisation has learnings to share with others</ListItem>
            </UnorderedList>
          </ListItem>
        </OrderedList>
        <Paragraph>All spreadsheets include a ‘Region’ column to allow DHSC regional leads to filter the data and see only those organisations within their region.</Paragraph>

        <H4>Individual organisation reports</H4>
        <Paragraph>You can select an organisation to download their individual data. You will not be able to do this until they have submitted their report.</Paragraph>
        <Paragraph>When you download an individual organisation report it will include the following spreadsheets:</Paragraph>
        <OrderedList>
          <ListItem>
            Report summary, showing high level information like:
            <UnorderedList>
              <ListItem>the organisation’s name</ListItem>
              <ListItem>reporting period</ListItem>
              <ListItem>deadline</ListItem>
            </UnorderedList>
          </ListItem>
          <ListItem>
            Activity results, showing the results for each activity including the organisation’s:
            <UnorderedList>
              <ListItem>progress against the measures they set</ListItem>
              <ListItem>challenges</ListItem>
              <ListItem>plans for the next quarter</ListItem>
            </UnorderedList>
          </ListItem>
          <ListItem>
            Funding spend, showing:
            <UnorderedList>
              <ListItem>actual and forecasted spend for each activity</ListItem>
              <ListItem>the organisation’s funding allocation</ListItem>
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
              <ListItem>whether the organisation has learnings to share with others</ListItem>
            </UnorderedList>
          </ListItem>
        </OrderedList>
      </>
    ),
  },
  {
    id: 'if-organisations-miss-a-reporting-deadline',
    title: 'If organisations miss a reporting deadline',
    content: (
      <>
        <Paragraph>
          They should email{' '}
          <a className="govuk-link" href={GUIDANCE_SUPPORT_MAILTO} data-testid="admin-guidance-link-email-missed-deadline">
            {GUIDANCE_SUPPORT_EMAIL}
          </a>{' '}
          as soon as possible.
        </Paragraph>
        <Paragraph>DHSC will tell them whether the reporting window can be reopened or whether alternative arrangements are available.</Paragraph>
      </>
    ),
  },
  {
    id: 'if-you-need-help-using-the-service',
    title: 'If you need help using the service',
    content: (
      <>
        <Paragraph>
          You can join us on Teams at{' '}
          <a className="govuk-link" href={GUIDANCE_TEAMS_CHANNEL_URL} data-testid="admin-guidance-link-teams">
            {GUIDANCE_TEAMS_CHANNEL_NAME}
          </a>{' '}
          where we will be sharing resources and answering questions.
        </Paragraph>
        <Paragraph>The Teams channel includes a troubleshooting guide for any technical issues.</Paragraph>
        <Paragraph>
          For technical problems or urgent questions about using the service, email{' '}
          <a className="govuk-link" href={GUIDANCE_SUPPORT_MAILTO} data-testid="admin-guidance-link-email-support">
            {GUIDANCE_SUPPORT_EMAIL}
          </a>
          .
        </Paragraph>
      </>
    ),
  },
];
