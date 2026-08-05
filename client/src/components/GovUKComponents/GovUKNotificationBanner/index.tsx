import React, { useState, useEffect } from 'react';

interface GovUKNotificationBannerProps {
  type?: 'success' | 'important';
  title: string;
  children: React.ReactNode;
  autoDismiss?: boolean;
  autoDismissTimeout?: number;
  testId?: string;
}

function GovUKNotificationBanner({
  type = 'important',
  title,
  children,
  autoDismiss = false,
  autoDismissTimeout = 5000,
  testId,
}: GovUKNotificationBannerProps): React.ReactElement | null {
  const [isVisible, setIsVisible] = useState(true);
  const isSuccess = type === 'success';

  useEffect(() => {
    if (autoDismiss) {
      const timer = setTimeout(() => {
        setIsVisible(false);
      }, autoDismissTimeout);

      return () => clearTimeout(timer);
    }
  }, [autoDismiss, autoDismissTimeout]);

  if (!isVisible) {
    return null;
  }

  // Derive the title id from the testId when one is given so two banners on the same page (e.g. a success
  // banner alongside an important one) don't share a DOM id / aria-labelledby target.
  const titleId = testId ? `${testId}-title` : 'govuk-notification-banner-title';

  return (
    <div
      className={`govuk-notification-banner ${isSuccess ? 'govuk-notification-banner--success' : ''}`}
      role={isSuccess ? 'alert' : 'region'}
      aria-labelledby={titleId}
      data-module="govuk-notification-banner"
      data-testid={testId ?? (isSuccess ? 'notification-banner-success' : 'notification-banner')}>
      <div className="govuk-notification-banner__header">
        <h2 className="govuk-notification-banner__title" id={titleId}>
          {isSuccess ? 'Success' : title}
        </h2>
      </div>
      <div className="govuk-notification-banner__content">
        {isSuccess && <h3 className="govuk-notification-banner__heading">{title}</h3>}
        {children}
      </div>
    </div>
  );
}

export default GovUKNotificationBanner;
