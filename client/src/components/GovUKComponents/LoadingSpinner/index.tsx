import React from 'react';
import { LoadingBox } from 'govuk-react';

interface LoadingSpinnerProps {
  loading?: boolean;
  children?: React.ReactNode;
  label?: string;
  testId?: string;
}

const LoadingSpinner = ({ loading = true, children, label, testId = 'loading-spinner' }: LoadingSpinnerProps): React.ReactElement => {
  return (
    <div data-testid={loading ? testId : undefined}>
      <LoadingBox loading={loading} title={label}>
        {children}
      </LoadingBox>
    </div>
  );
};

export default LoadingSpinner;
