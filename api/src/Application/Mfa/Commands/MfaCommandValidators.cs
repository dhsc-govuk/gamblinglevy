using Application.Common.Validation;
using FluentValidation;

namespace Application.Mfa.Commands;

// Validators for the MFA commands. Kept in one file because each is two lines and they share a single
// rationale, and because the commands themselves live as flat files in this folder rather than in the
// per-action folders the rest of the Application layer uses.
//
// These bound the length and nothing else. The exact code shape is checked inside the handlers — see
// MfaCodeFormat for why a validator is the wrong place for it (BLUM-7240 / CWE-20).
//
// SetupMfaCommand and LogoutMfaSessionCommand deliberately have no validator. Neither carries a field
// supplied by the caller: Setup takes only the user id resolved from the token, and Logout takes a
// nullable session id read from the request's own cookie, whose absence is a documented path through the
// handler rather than an error.
public class VerifyMfaCommandValidator : AbstractValidator<VerifyMfaCommand>
{
    public VerifyMfaCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(FieldLimits.TotpCode);
    }
}

public class VerifyMfaSetupCommandValidator : AbstractValidator<VerifyMfaSetupCommand>
{
    public VerifyMfaSetupCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(FieldLimits.TotpCode);
    }
}

public class DisableMfaCommandValidator : AbstractValidator<DisableMfaCommand>
{
    public DisableMfaCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(FieldLimits.TotpCode);
    }
}

public class RegenerateRecoveryCodesCommandValidator : AbstractValidator<RegenerateRecoveryCodesCommand>
{
    public RegenerateRecoveryCodesCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(FieldLimits.TotpCode);
    }
}

// The most valuable bound of the five. VerifyRecoveryCodeCommandHandler answers by running BCrypt at cost 12
// over every stored hash — roughly two and a half CPU-seconds per request — and an unbounded string reached
// that loop. It was also, until BLUM-7239, the one MFA endpoint with no per-minute limiter in front of it;
// all five now reserve against IMfaRateLimitService before doing any work.
public class VerifyRecoveryCodeCommandValidator : AbstractValidator<VerifyRecoveryCodeCommand>
{
    public VerifyRecoveryCodeCommandValidator()
    {
        RuleFor(x => x.RecoveryCode).NotEmpty().MaximumLength(FieldLimits.RecoveryCode);
    }
}

public class RevokeMfaSessionCommandValidator : AbstractValidator<RevokeMfaSessionCommand>
{
    public RevokeMfaSessionCommandValidator()
    {
        RuleFor(x => x.SessionId)
            .NotNull()
            .Must(sessionId => sessionId is null || sessionId.Value != Guid.Empty)
            .WithMessage("Select a session to sign out.");
    }
}