using System.Security.Cryptography;
using Application.Common.Interfaces;
using OtpNet;

namespace Infrastructure.Mfa;

public class TotpService : ITotpService
{
    private const int SecretLength = 20;
    private const int TotpStep = 30;
    private const int TotpDigits = 6;
    private const int VerificationWindowSteps = 1;

    public string GenerateSecret()
    {
        var secretBytes = new byte[SecretLength];
        RandomNumberGenerator.Fill(secretBytes);
        return Base32Encoding.ToString(secretBytes);
    }

    public string GenerateQrCodeUri(string email, string secret, string issuer = "Gambling Levy Portal")
    {
        var encodedIssuer = Uri.EscapeDataString(issuer);
        var encodedEmail = Uri.EscapeDataString(email);
        return $"otpauth://totp/{encodedIssuer}:{encodedEmail}?secret={secret}&issuer={encodedIssuer}&algorithm=SHA1&digits={TotpDigits}&period={TotpStep}";
    }

    public bool ValidateCode(string secret, string code)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        try
        {
            var secretBytes = Base32Encoding.ToBytes(secret);
            var totp = new Totp(secretBytes, step: TotpStep, totpSize: TotpDigits);

            return totp.VerifyTotp(code, out _, new VerificationWindow(VerificationWindowSteps, VerificationWindowSteps));
        }
        catch
        {
            return false;
        }
    }
}