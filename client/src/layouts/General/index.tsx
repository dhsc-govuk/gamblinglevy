import React from 'react';
import { Box } from '@mui/material';
import LayoutHeader from './LayoutHeader';
import LayoutFooter from './LayoutFooter';
import Content from './Content';
import { GovUKSkipLink } from '../../components/GovUKComponents';
import type { HeaderNavItem } from '../../components/AuthProvider';

// GOV.UK Colors
const govukWhite = '#ffffff';

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
    <Box
      sx={{
        minHeight: '100vh',
        display: 'flex',
        flexDirection: 'column',
        backgroundColor: govukWhite,
      }}>
      <GovUKSkipLink />
      <LayoutHeader navigation={navigation} />
      <Content breadcrumbs={breadcrumbs ?? []} endContent={endContent} backLink={backLink} isMainContentTarget={mainContentTarget === 'layout'}>
        {children}
      </Content>
      <LayoutFooter />
    </Box>
  );
};

export default GeneralLayout;
