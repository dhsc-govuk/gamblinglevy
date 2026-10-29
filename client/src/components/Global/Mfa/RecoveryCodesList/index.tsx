import React from 'react';
import { GovUKButton } from '../../../GovUKComponents';
import { saveBlob } from '../../../../helpers/downloadFile';
import { composeTestId } from '../../../../helpers/testIds';
import './styles.css';

interface Props {
  codes: string[];
  testId: string;
}

// The recovery codes plus their Copy and Download buttons, shared by the last step of views/Mfa/Setup and by
// views/Mfa/RecoveryCodes. Only those two lazy pages import it, so it stays out of the entry bundle.
const RecoveryCodesList = ({ codes, testId }: Props): React.ReactElement => {
  const text = codes.join('\n');

  // writeText rejects without a secure context or with clipboard permission denied. The codes are on screen and
  // Download still works, so a refusal is swallowed rather than left as an unhandled rejection.
  const handleCopy = (): void => {
    navigator.clipboard?.writeText(text).catch(() => undefined);
  };

  const handleDownload = (): void => {
    saveBlob(new Blob([text], { type: 'text/plain' }), 'recovery-codes.txt');
  };

  return (
    <div data-testid={testId}>
      <div className="mfa-recovery-codes">
        <ul className="mfa-recovery-codes__list">
          {codes.map((code) => (
            <li key={code} className="mfa-recovery-codes__code">
              {code}
            </li>
          ))}
        </ul>
      </div>

      <div className="govuk-button-group">
        <GovUKButton className="govuk-button govuk-button--secondary" onClick={handleCopy} data-testid={composeTestId(testId, 'copy')}>
          Copy codes
        </GovUKButton>
        <GovUKButton className="govuk-button govuk-button--secondary" onClick={handleDownload} data-testid={composeTestId(testId, 'download')}>
          Download codes
        </GovUKButton>
      </div>
    </div>
  );
};

export default RecoveryCodesList;
