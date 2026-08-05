namespace Application.Common.Interfaces;

public interface ITotpService
{
    string GenerateSecret();
    string GenerateQrCodeUri(string email, string secret, string issuer = "Gambling Levy Portal");
    bool ValidateCode(string secret, string code);
}