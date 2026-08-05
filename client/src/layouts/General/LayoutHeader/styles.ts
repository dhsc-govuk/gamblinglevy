import { Box, Typography, styled } from '@mui/material';

// GOV.UK Colors
export const govukBlack = '#0b0c0c';
export const govukBlue = '#1d70b8';
export const govukWhite = '#ffffff';
export const govukLightGrey = '#f3f2f1';
export const govukLightBlue = '#f4f8fb';
export const govukMidGrey = '#b1b4b6';
export const govukBorderBlue = '#8eb8dc';

// Header (Blue bar with GOV.UK logo)
export const Header = styled(Box)(() => ({
  backgroundColor: govukBlue,
  boxSizing: 'border-box',
  height: '60px',
  padding: '12px 0 18px',
}));

export const HeaderContainer = styled(Box)(() => ({
  maxWidth: '990px',
  margin: '0 auto',
  padding: '0 15px',
}));

export const LogoWrapper = styled(Box)(() => ({
  display: 'flex',
  alignItems: 'center',
  gap: '6px',
}));

export const LogoText = styled(Typography)(({ theme }) => ({
  fontFamily: '"GDS Transport", arial, sans-serif',
  fontWeight: 700,
  fontSize: '30px',
  color: govukWhite,
  lineHeight: 1,
  [theme.breakpoints.down('sm')]: {
    fontSize: '24px',
  },
}));

// Service Banner (Light blue bar with service name)
export const ServiceBanner = styled(Box)(() => ({
  backgroundColor: govukLightBlue,
  padding: '15px 0',
  borderBottom: `2px solid ${govukBorderBlue}`,
}));

export const ServiceBannerContainer = styled(Box)(({ theme }) => ({
  maxWidth: '960px',
  margin: '0 auto',
  padding: '0 15px',
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'space-between',
  gap: '20px',
  [theme.breakpoints.down('md')]: {
    flexDirection: 'column',
    alignItems: 'flex-start',
    gap: '10px',
  },
}));

export const ServiceName = styled(Typography)(({ theme }) => ({
  fontFamily: '"GDS Transport", arial, sans-serif',
  fontWeight: 700,
  fontSize: '19px',
  color: govukBlack,
  [theme.breakpoints.down('sm')]: {
    fontSize: '16px',
  },
}));

// Sign-out link in the blue header bar (matches the GOV.UK header user-link pattern)
export const SignOutLink = styled('button')(() => ({
  fontFamily: '"GDS Transport", arial, sans-serif',
  fontSize: '19px',
  color: govukWhite,
  backgroundColor: 'transparent',
  border: 'none',
  padding: 0,
  margin: 0,
  cursor: 'pointer',
  textDecoration: 'underline',
  textUnderlineOffset: '0.1em',
  '&:hover': {
    textDecorationThickness: '3px',
  },
  '&:focus': {
    outline: '3px solid #ffdd00',
    outlineOffset: 0,
    backgroundColor: '#ffdd00',
    color: govukBlack,
    textDecoration: 'none',
  },
}));
