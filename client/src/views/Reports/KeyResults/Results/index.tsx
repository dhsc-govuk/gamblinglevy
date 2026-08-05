import React from 'react';
import ActivityReportQuestionsPage from '../ActivityReportQuestionsPage';

// "Activity results" — the 'results' questions for one activity. People-and-roles activities have no
// results questions and redirect straight to the delivery page.
const Results = (): React.ReactElement => <ActivityReportQuestionsPage section="results" />;

export default Results;
