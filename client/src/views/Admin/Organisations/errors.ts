type ValidationErrorEnvelope = {
  errors?: Record<string, string[]> | { property?: string; message?: string }[];
  detail?: string;
  error?: string;
  message?: string;
  title?: string;
};

export type OrganisationFormErrors = Record<string, string | undefined>;

// Keyed on the C# property name: that is what the validation_failed envelope carries, and it is what routes
// a server refusal onto the right field instead of collapsing it onto the submit button.
const fieldNameMap: Record<string, string> = {
  ActivateImmediately: 'activateImmediately',
  AllocatedAmount: 'allocatedAmount',
  ECode: 'eCode',
  FinancialYear: 'financialYear',
  Name: 'name',
  NextYearAllocatedAmount: 'nextYearAllocatedAmount',
  OnsCode: 'onsCode',
  Region: 'region',
  Type: 'type',
};

const requiredMessages: Record<string, string> = {
  AllocatedAmount: 'Enter the annual allocation',
  FinancialYear: 'Select a financial year',
  Name: 'Enter the organisation name',
  Region: "Select the organisation's region",
  Type: 'Choose LA or VCSE',
};

const friendlyMessage = (field: string, message: string): string => {
  // Two shapes reach here: ASP.NET model binding says "field is required", FluentValidation's bare
  // NotEmpty() says "must not be empty". Name, Region and Type rely on the latter.
  if (message.includes('field is required') || message.includes('must not be empty')) {
    return requiredMessages[field] ?? message;
  }

  if (field === 'AllocatedAmount' && message.includes('greater than 0')) {
    return 'Annual allocation must be a number greater than 0';
  }

  // The backend labels this rule "Annual Allocation" for both amounts, so the raw message would name the wrong
  // field on screen. MaxMoney and precision are both reachable: the client validator bounds neither.
  if (field === 'NextYearAllocatedAmount') {
    if (message.includes('greater than 0')) return "The upcoming year's annual allocation must be a number greater than 0";
    if (message.includes('or less')) return "The upcoming year's annual allocation is too large";
    if (message.includes('decimal places')) return "The upcoming year's annual allocation must have no more than 2 decimal places";
  }

  if (field === 'AllocatedAmount') {
    if (message.includes('or less')) return 'Annual allocation is too large';
    if (message.includes('decimal places')) return 'Annual allocation must be a number with no more than 2 decimal places';
  }

  // Matched on the invariant part of the sentence, not the example year it happens to quote.
  if (field === 'FinancialYear' && message.includes('financial year in the format')) {
    return 'Select a financial year';
  }

  if (field === 'Region' && message.includes('supported English regions')) {
    return 'Select a supported English region';
  }

  if (field === 'Type' && message.includes('LA or VCSE')) {
    return 'Choose LA or VCSE';
  }

  return message;
};

const isValidationEnvelope = (value: unknown): value is ValidationErrorEnvelope => typeof value === 'object' && value !== null;

export const organisationServerErrors = (data: unknown, fallbackMessage: string): OrganisationFormErrors => {
  if (typeof data === 'string') {
    return { submit: data || fallbackMessage };
  }

  if (!isValidationEnvelope(data)) {
    return { submit: fallbackMessage };
  }

  if (Array.isArray(data.errors)) {
    const next: OrganisationFormErrors = {};
    data.errors.forEach((error) => {
      if (!error.property || !error.message) return;
      next[fieldNameMap[error.property] ?? 'submit'] = friendlyMessage(error.property, error.message);
    });
    return Object.keys(next).length > 0 ? next : { submit: fallbackMessage };
  }

  if (data.errors) {
    const next = Object.entries(data.errors).reduce<OrganisationFormErrors>((acc, [field, messages]) => {
      const message = messages.find(Boolean);
      if (!message) return acc;
      acc[fieldNameMap[field] ?? 'submit'] = friendlyMessage(field, message);
      return acc;
    }, {});

    if (Object.keys(next).length > 0) {
      return next;
    }
  }

  return { submit: data.message ?? data.detail ?? data.error ?? fallbackMessage };
};
