// GOV.UK colours for the few places that need one in TypeScript rather than CSS.
// Based on GOV.UK Design System colour palette
// https://design-system.service.gov.uk/styles/colour/
export const govukColors = {
  // Main colours
  blue: '#1d70b8', // $govuk-brand-colour
  black: '#0b0c0c', // $govuk-text-colour
  white: '#ffffff', // govuk-colour("white")
  red: '#d4351c', // $govuk-error-colour
  yellow: '#ffdd00', // govuk-colour("yellow")
  green: '#00703c', // $govuk-success-colour

  // Grey scale
  grey1: '#505a5f', // $govuk-secondary-text-colour / govuk-colour("dark-grey")
  grey2: '#b1b4b6', // $govuk-border-colour / govuk-colour("mid-grey")
  grey3: '#f3f2f1', // govuk-colour("light-grey")
  // The two below are GOV.UK colours this palette did not list until BLUM-8758 used them in CSS.
  grey4: '#cecece', // govuk-frontend 6's $govuk-border-colour (grey2 is the v5 value) — the home-page card borders
  grey5: '#dbdad9', // govuk-frontend 5's secondary-button hover, govuk-shade(light-grey, 10%) — the action menu's hover

  // Extended palette
  orange: '#f47738', // govuk-colour("orange") - for warnings
  turquoise: '#28a197', // govuk-colour("turquoise")
};
