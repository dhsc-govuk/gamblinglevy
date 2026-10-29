import React from 'react';
import LayoutHeader from './LayoutHeader';
import LayoutFooter from './LayoutFooter';
import Content from './Content';
import { GovUKSkipLink } from '../../components/GovUKComponents';
import type { HeaderNavItem } from '../../components/AuthProvider';
import './styles.css';

interface Props extends React.PropsWithChildren {
  breadcrumbs?: { label: string; link: string }[];
  endContent?: React.ReactElement;
  navigation?: HeaderNavItem[];
  // `href` is optional so a JS-only step-back (multi-step wizards) can render the button form of the back link.
  backLink?: { href?: string; onClick?: () => void; testId?: string; className?: string };
  mainContentTarget?: 'layout' | 'nested';
}

const GeneralLayout = ({ children, breadcrumbs, endContent, navigation, backLink, mainContentTarget = 'layout' }: Props): React.ReactElement => {
  return (
    <div className="app-general-layout">
      <GovUKSkipLink />
      {/* LayoutHeader renders the beta phase banner as the last thing in the `banner` landmark, so it appears
          on every page of the service (BLUM-7102) above <main>. Do not add a second one here. */}
      <LayoutHeader navigation={navigation} />
      <Content breadcrumbs={breadcrumbs ?? []} endContent={endContent} backLink={backLink} isMainContentTarget={mainContentTarget === 'layout'}>
        {children}
      </Content>
      <LayoutFooter />
    </div>
  );
};

export default GeneralLayout;
