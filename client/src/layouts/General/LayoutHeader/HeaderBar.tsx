import React from 'react';
import './styles.css';

/**
 * The blue GOV.UK bar at the top of GeneralLayout, and of the Cookies and Accessibility statement pages, which
 * render it without the sign-out link or service navigation. Not GovUKHeader: .govuk-header__logo's padding would
 * change this bar's height, and LayoutHeader needs room for the account links beside the crown.
 */
const HeaderBar = ({ children }: React.PropsWithChildren): React.ReactElement => (
  <div className="app-header-bar">
    <div className="app-header-bar__container">{children}</div>
  </div>
);

export default HeaderBar;
