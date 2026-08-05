type ValidationErrorEnvelope = {
  errors?: Record<string, string[]> | { property?: string; message?: string }[];
  detail?: string;
  error?: string;
  message?: string;
  title?: string;
};

export type OrganisationFormErrors = Record<string, string | undefined>;

const fieldNameMap: Record<string, string> = {
  AllocatedAmount: 'allocatedAmount',
  ECode: 'eCode',
  Name: 'name',
  OnsCode: 'onsCode',
  Region: 'region',
  Type: 'type',
};

const requiredMessages: Record<string, string> = {
  AllocatedAmount: 'Enter the annual allocation',
  Name: 'Enter the organisation name',
  Region: 'Choose a region',
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

  if (field === 'Region' && message.includes('supported English regions')) {
    return 'Choose a supported English region';
  }

  if (field === 'Type' && message.includes('LA or VCSE')) {
    return 'Choose LA or VCSE';
  }

  return message;
};

const isValidationEnvelope = (value: unknown): value is ValidationErrorEnvelope =>
  typeof value === 'object' && value !== null;

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
