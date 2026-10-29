import eslint from '@eslint/js';
import jsxA11y from 'eslint-plugin-jsx-a11y';
import react from 'eslint-plugin-react';
import reactHooks from 'eslint-plugin-react-hooks';
import globals from 'globals';
import tseslint from 'typescript-eslint';

export default tseslint.config(
  {
    ignores: ['dist/**'],
  },
  eslint.configs.recommended,
  ...tseslint.configs.recommended,
  react.configs.flat.recommended,
  react.configs.flat['jsx-runtime'],
  jsxA11y.flatConfigs.recommended,
  {
    // The static server that sets the security headers on the Azure deployments, its shared
    // policy definition and the build scripts. Node globals, not browser ones.
    files: ['server.mjs', 'security-headers.mjs', 'scripts/**/*.mjs'],
    languageOptions: {
      globals: globals.node,
    },
  },
  {
    files: ['src/**/*.{js,jsx,ts,tsx}'],
    languageOptions: {
      globals: globals.browser,
    },
    plugins: {
      'react-hooks': reactHooks,
    },
    settings: {
      react: {
        version: 'detect',
      },
    },
    rules: {
      'react-hooks/rules-of-hooks': 'error',
      'react-hooks/exhaustive-deps': 'warn',
      // GovUKErrorSummary takes the focus its trigger asks for only if it was already mounted when the trigger
      // was bumped, so it is always rendered and hidden with `errors={[]}`. As the direct result of `&&`, `||` or
      // a ternary it mounts together with its error and silently misses that focus, which neither tsc nor a
      // reviewer reliably notices. What these selectors cannot see is a summary nested deeper inside such a
      // branch (wrapped in a fragment or an element): that is safe only while nothing reveals the branch in the
      // same update that bumps the trigger — otherwise hoist it above the branch (ActivityWizard).
      'no-restricted-syntax': [
        'error',
        {
          selector: ':matches(LogicalExpression, ConditionalExpression) > JSXElement[openingElement.name.name=/^(GovUK)?ErrorSummary$/]',
          message: 'Render ErrorSummary unconditionally and pass an empty `errors` array to hide it. A conditionally rendered summary misses the focus its focusTrigger asked for.',
        },
        {
          selector: ':matches(LogicalExpression, ConditionalExpression) > JSXElement[openingElement.name.property.name=/^(GovUK)?ErrorSummary$/]',
          message: 'Render ErrorSummary unconditionally and pass an empty `errors` array to hide it. A conditionally rendered summary misses the focus its focusTrigger asked for.',
        },
      ],
      'no-unused-vars': 'off',
      '@typescript-eslint/no-empty-object-type': 'off',
      '@typescript-eslint/no-unused-vars': [
        'error',
        {
          argsIgnorePattern: '^_',
          caughtErrorsIgnorePattern: '^_',
          varsIgnorePattern: '^_',
        },
      ],
      'jsx-a11y/no-noninteractive-element-interactions': [
        'error',
        {
          handlers: ['onClick', 'onMouseDown', 'onMouseUp', 'onKeyPress', 'onKeyDown', 'onKeyUp'],
        },
      ],
    },
  }
);
