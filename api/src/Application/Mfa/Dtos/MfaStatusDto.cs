namespace Application.Mfa.Dtos;

public enum MfaState
{
    None,
    PendingSetup,
    Enabled,
    Disabled,
}

public record MfaStatusDto(
    bool IsEnabled,
    DateTime? EnabledAt,
    int RecoveryCodesRemaining,
    MfaState State);