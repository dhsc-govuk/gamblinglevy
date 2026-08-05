import { Box, styled } from '@mui/material';
import { Link as RouterLink } from 'react-router-dom';

// GOV.UK Colors
const govukBlack = '#0b0c0c';
const govukLightBlue = '#f4f8fb';
const govukBlue = '#1d70b8';

export const FooterRoot = styled(Box)(() => ({
  boxSizing: 'border-box',
  height: '314px',
  overflow: 'hidden',
  backgroundColor: govukLightBlue,
  borderTop: `10px solid ${govukBlue}`,
  marginTop: 'auto',
  '@media (max-width: 640px)': {
    height: 'auto',
    overflow: 'visible',
  },
}));

export const FooterContainer = styled(Box)(() => ({
  boxSizing: 'border-box',
  width: '100%',
  height: '100%',
  maxWidth: '990px',
  margin: '0 auto',
  padding: '50px 15px 40px',
  '& .app-footer__crown': {
    marginBottom: '30px',
  },
  '& .app-footer__meta-content': {
    flex: 1,
    minWidth: 0,
  },
  '@media (max-width: 640px)': {
    height: 'auto',
    padding: '40px 15px',
  },
}));

export const FooterSection = styled(Box)(() => ({
  marginBottom: '30px',
}));

export const FooterHeading = styled('h2')(() => ({
  fontFamily: '"GDS Transport", arial, sans-serif',
  fontSize: '19px',
  fontWeight: 700,
  color: govukBlack,
  marginBottom: '20px',
  marginTop: 0,
  paddingBottom: '10px',
  borderBottom: '1px solid #b1b4b6',
}));

export const FooterList = styled('ul')(() => ({
  listStyle: 'none',
  padding: 0,
  margin: 0,
  columnCount: 2,
  columnGap: '30px',
}));

export const FooterListItem = styled('li')(() => ({
  marginBottom: '10px',
  breakInside: 'avoid',
}));

export const FooterLink = styled('a')(() => ({
  fontFamily: '"GDS Transport", arial, sans-serif',
  fontSize: '16px',
  color: govukBlack,
  textDecoration: 'underline',
  textDecorationColor: govukBlack,
  '&:hover': {
    textDecorationThickness: '3px',
    textUnderlineOffset: '0.1em',
  },
}));

export const FooterDivider = styled(Box)(() => ({
  borderTop: '1px solid #b1b4b6',
  marginTop: '30px',
  marginBottom: '25px',
}));

export const FooterMeta = styled(Box)(() => ({
  boxSizing: 'border-box',
  height: '153px',
  padding: '3px 0 25px',
  display: 'flex',
  justifyContent: 'space-between',
  alignItems: 'flex-end',
  flexWrap: 'wrap',
  gap: '15px',
  '@media (max-width: 640px)': {
    height: 'auto',
    flexDirection: 'column',
    alignItems: 'flex-start',
  },
}));

export const FooterMetaLinks = styled(Box)(() => ({
  display: 'flex',
  flexWrap: 'wrap',
  gap: '15px',
}));

const footerMetaLinkStyles = {
  fontFamily: '"GDS Transport", arial, sans-serif',
  fontSize: '16px',
  lineHeight: '20px',
  color: `${govukBlack} !important`,
  textDecoration: 'underline',
  '&:visited': {
    color: `${govukBlack} !important`,
  },
  '&:link': {
    color: `${govukBlack} !important`,
  },
  '&:hover': {
    color: `${govukBlack} !important`,
    textDecorationThickness: '3px',
    textUnderlineOffset: '0.1em',
  },
};

export const FooterMetaLink = styled('a')(() => footerMetaLinkStyles);

// Same visual treatment as FooterMetaLink, but an in-app react-router link (SPA navigation, no new tab)
// for internal pages such as the cookies page.
export const FooterMetaRouterLink = styled(RouterLink)(() => footerMetaLinkStyles);

export const FooterLicence = styled(Box)(() => ({
  display: 'flex',
  alignItems: 'flex-start',
  gap: '10px',
  marginTop: '15px',
}));

export const FooterLicenceText = styled('span')(() => ({
  fontFamily: '"GDS Transport", arial, sans-serif',
  fontSize: '16px',
  lineHeight: '20px',
  color: govukBlack,
  '& a': {
    color: `${govukBlack} !important`,
    textDecoration: 'underline',
    '&:link': {
      color: `${govukBlack} !important`,
    },
    '&:visited': {
      color: `${govukBlack} !important`,
    },
    '&:hover': {
      color: `${govukBlack} !important`,
    },
  },
}));

export const OglLogoWrapper = styled(Box)(() => ({
  display: 'flex',
  alignItems: 'center',
  gap: '10px',
  fontFamily: '"GDS Transport", arial, sans-serif',
  fontSize: '24px',
  fontWeight: 700,
  lineHeight: 1,
}));

export const CopyrightLogo = styled(Box)(() => ({
  display: 'flex',
  flexDirection: 'column',
  alignItems: 'center',
  gap: '5px',
}));

export const CopyrightText = styled('a')(() => ({
  fontFamily: '"GDS Transport", arial, sans-serif',
  fontSize: '16px',
  lineHeight: '20px',
  color: `${govukBlack} !important`,
  textDecoration: 'underline',
  '&:link': {
    color: `${govukBlack} !important`,
  },
  '&:visited': {
    color: `${govukBlack} !important`,
  },
  '&:hover': {
    color: `${govukBlack} !important`,
    textDecorationThickness: '3px',
    textUnderlineOffset: '0.1em',
  },
}));
