using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Mfa.Dtos;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OtpNet;
using Tests.Common;
using Tests.Data;
using Xunit;

namespace Api.Tests.Integration.Mfa;

/// <summary>
/// Regression coverage: IMfaEncryptionService.Decrypt (wraps IDataProtector.Unprotect) throws
/// CryptographicException when a secret was protected with a key the current key ring no longer has — e.g.
/// after key rotation, or an ephemeral key ring lost on an App Service container restart. Before the fix,
/// none of the four handlers that call Decrypt caught it, so it escaped the handler's
/// Either&lt;MfaException, TResult&gt; return type, and UnhandledExceptionBehaviour — which logs then
/// rethrows — let it become a raw unhandled 500 with no MFA_* envelope.
///
/// Simulated by corrupting a real UserMfa row's EncryptedSecret after setup, so Decrypt genuinely throws
/// rather than being mocked — the real Data Protection provider is wired up in the test host too.
/// </summary>
public class MfaDecryptionFailureTests : BaseIntegrationTest, IAsyncLifetime
{
    public MfaDecryptionFailureTests(IntegrationTestWebFactory factory)
        : base(factory)
    {
    }

    public async Task InitializeAsync()
    {
        Context.MfaSessions.RemoveRange(Context.MfaSessions);
        Context.UserMfas.RemoveRange(Context.UserMfas);
        Context.AuditLogs.RemoveRange(Context.AuditLogs);
        Context.Users.RemoveRange(Context.Users);
        await Context.SaveChangesAsync();

        await Context.Users.AddAsync(AdminsData.MainAdmin);
        await Context.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Verify_WithUndecryptableSecret_ReturnsWellFormedMfaErrorEnvelope_NotAnUnhandledCrash()
    {
        await CompleteSetupAsync();
        await CorruptStoredSecretAsync();

        var response = await Client.PostAsJsonAsync("/mfa/verify", new MfaVerifyRequestDto("123456"));

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("error").GetString().Should().Be(
            "MFA_ERROR",
            "a decrypt failure should answer the deliberate MfaUnknownException envelope, not the framework's raw unhandled-exception response");
        body.RootElement.GetProperty("message").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Verify_WithUndecryptableSecret_DoesNotCountTowardsAccountLockout()
    {
        // A decrypt failure is an infrastructure fault, not a wrong guess: MfaFailureRecorder — which
        // increments UserMfa.FailedAttempts and would eventually lock the account — is only reached from the
        // "code didn't match" branch, not from the catch this test exercises. Only the in-memory per-minute
        // damper (MfaAttemptGate.ReserveAsync, charged before the secret is even loaded) is spent by this
        // request; the persisted counter that actually locks the account must stay untouched.
        await CompleteSetupAsync();
        await CorruptStoredSecretAsync();

        await Client.PostAsJsonAsync("/mfa/verify", new MfaVerifyRequestDto("123456"));

        var mfa = await Context.UserMfas.AsNoTracking().FirstAsync(x => x.UserId == AdminsData.MainAdmin.Id);
        mfa.FailedAttempts.Should().Be(0);
        mfa.LockedUntil.Should().BeNull();
    }

    private async Task CompleteSetupAsync()
    {
        var setupResponse = await Client.PostAsync("/mfa/setup", content: null);
        var setup = await setupResponse.ToResponseModel<MfaSetupResponseDto>();
        var code = GenerateTotpCode(setup.ManualEntryKey);

        var verifyResponse = await Client.PostAsJsonAsync("/mfa/setup/verify", new MfaVerifyRequestDto(code));
        verifyResponse.IsSuccessStatusCode.Should().BeTrue();
    }

    /// <summary>
    /// Overwrites the real, successfully-protected secret with a payload IDataProtector cannot unprotect — the
    /// same shape of failure as a key the ring no longer has, without needing to fake key rotation. UserMfa
    /// exposes no setter for this, so it goes through the database directly, like
    /// MfaLockoutTests.ExpireLockAsync does for LockedUntil.
    /// </summary>
    private async Task CorruptStoredSecretAsync()
    {
        await Context.UserMfas
            .Where(x => x.UserId == AdminsData.MainAdmin.Id)
            .ExecuteUpdateAsync(x => x.SetProperty(m => m.EncryptedSecret, "not-a-real-protected-payload"));

        Context.ChangeTracker.Clear();
    }

    private static string GenerateTotpCode(string base32Secret)
    {
        var secretBytes = Base32Encoding.ToBytes(base32Secret);
        var totp = new Totp(secretBytes, step: 30, totpSize: 6);
        return totp.ComputeTotp();
    }
}