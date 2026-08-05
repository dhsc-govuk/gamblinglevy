using System.Net;
using System.Net.Http.Json;
using Application.Mfa.Dtos;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OtpNet;
using Tests.Common;
using Tests.Data;
using Xunit;

namespace Api.Tests.Integration.Mfa;

public class MfaControllerTests : BaseIntegrationTest, IAsyncLifetime
{
    public MfaControllerTests(IntegrationTestWebFactory factory) : base(factory) { }

    public async Task InitializeAsync()
    {
        Context.MfaSessions.RemoveRange(Context.MfaSessions);
        Context.UserMfas.RemoveRange(Context.UserMfas);
        Context.Users.RemoveRange(Context.Users);
        await Context.SaveChangesAsync();

        await Context.Users.AddAsync(AdminsData.MainAdmin);
        await Context.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Status_ReturnsNone_WhenNoMfaRecord()
    {
        var response = await Client.GetAsync("/mfa/status");

        response.IsSuccessStatusCode.Should().BeTrue();
        var status = await response.ToResponseModel<MfaStatusDto>();
        status.State.Should().Be(MfaState.None);
        status.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Setup_ReturnsQrCodeAndManualKey()
    {
        var response = await Client.PostAsync("/mfa/setup", content: null);

        response.IsSuccessStatusCode.Should().BeTrue();
        var setup = await response.ToResponseModel<MfaSetupResponseDto>();
        setup.QrCodeUri.Should().StartWith("otpauth://totp/");
        setup.QrCodeUri.Should().Contain("Gambling%20Levy%20Portal");
        setup.ManualEntryKey.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task SetupVerify_WithValidCode_EnablesMfaAndReturnsRecoveryCodes()
    {
        var setupResponse = await Client.PostAsync("/mfa/setup", content: null);
        var setup = await setupResponse.ToResponseModel<MfaSetupResponseDto>();
        var code = GenerateTotpCode(setup.ManualEntryKey);

        var verifyResponse = await Client.PostAsJsonAsync(
            "/mfa/setup/verify",
            new MfaVerifyRequestDto(code));

        verifyResponse.IsSuccessStatusCode.Should().BeTrue();
        var complete = await verifyResponse.ToResponseModel<MfaSetupCompleteResponseDto>();
        complete.Success.Should().BeTrue();
        complete.RecoveryCodes.Should().HaveCount(10);
        complete.SessionId.Should().NotBe(Guid.Empty);

        var statusResponse = await Client.GetAsync("/mfa/status");
        var status = await statusResponse.ToResponseModel<MfaStatusDto>();
        status.State.Should().Be(MfaState.Enabled);
        status.IsEnabled.Should().BeTrue();
        status.RecoveryCodesRemaining.Should().Be(10);
    }

    [Fact]
    public async Task SetupVerify_WithInvalidCode_Returns401()
    {
        await Client.PostAsync("/mfa/setup", content: null);

        var response = await Client.PostAsJsonAsync(
            "/mfa/setup/verify",
            new MfaVerifyRequestDto("000000"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Verify_WithValidCode_ReturnsSession()
    {
        var manualKey = await CompleteSetupAsync();
        var code = GenerateTotpCode(manualKey);

        var response = await Client.PostAsJsonAsync(
            "/mfa/verify",
            new MfaVerifyRequestDto(code));

        response.IsSuccessStatusCode.Should().BeTrue();
        var result = await response.ToResponseModel<MfaVerifyResponseDto>();
        result.Success.Should().BeTrue();
        result.SessionId.Should().NotBe(Guid.Empty);
    }

    // BLUM-7240 / CWE-20 — a malformed code is rejected on its shape before the secret is decrypted, but on
    // the same branch as a wrong one.
    //
    // The status assertion is the easy half. The failed-attempt assertion is the point: had the shape check
    // gone in a FluentValidation validator, the request would have returned 400 before the handler ran, so it
    // would never have reached MfaRateLimitService.RecordAttempt, UserMfa.RecordFailedAttempt or the
    // mfa.verify.failed audit append — quietly disabling the lockout for malformed traffic and deleting an
    // existing security trail, in a ticket whose whole purpose is to add one.
    [Theory]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("abcdef")]
    [InlineData("12 456")]
    public async Task Verify_WithMalformedCode_StillCountsAsAFailedAttempt(string malformed)
    {
        await CompleteSetupAsync();

        var response = await Client.PostAsJsonAsync("/mfa/verify", new MfaVerifyRequestDto(malformed));

        // The MFA envelope the client already parses, not the validation_failed 400 — views/Mfa/Verify
        // branches only on the MFA_* codes.
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var mfa = await Context.UserMfas.AsNoTracking().FirstAsync(x => x.UserId == AdminsData.MainAdmin.Id);
        mfa.FailedAttempts.Should().Be(1);
    }

    [Fact]
    public async Task VerifyRecoveryCode_WithMalformedCode_StillCountsAsAFailedAttempt()
    {
        await CompleteSetupWithCodesAsync();

        // Nothing of this shape can match a stored hash, so refusing it early skips a BCrypt loop that costs
        // roughly a quarter of a second per stored code on an endpoint with no per-minute limiter.
        var response = await Client.PostAsJsonAsync("/mfa/verify/recovery", new MfaVerifyRequestDto("NOT-A-CODE"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var mfa = await Context.UserMfas.AsNoTracking().FirstAsync(x => x.UserId == AdminsData.MainAdmin.Id);
        mfa.FailedAttempts.Should().Be(1);
    }

    [Fact]
    public async Task Verify_RecoveryCode_ConsumesItOnFirstUse()
    {
        var (manualKey, recoveryCodes) = await CompleteSetupWithCodesAsync();
        var firstCode = recoveryCodes[0];

        var firstResponse = await Client.PostAsJsonAsync(
            "/mfa/verify/recovery",
            new MfaVerifyRequestDto(firstCode));
        firstResponse.IsSuccessStatusCode.Should().BeTrue();

        var secondResponse = await Client.PostAsJsonAsync(
            "/mfa/verify/recovery",
            new MfaVerifyRequestDto(firstCode));
        secondResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var statusResponse = await Client.GetAsync("/mfa/status");
        var status = await statusResponse.ToResponseModel<MfaStatusDto>();
        status.RecoveryCodesRemaining.Should().Be(9);
    }

    [Fact]
    public async Task Disable_WithValidCode_RemovesEnabledState()
    {
        var manualKey = await CompleteSetupAsync();
        var code = GenerateTotpCode(manualKey);

        var response = await Client.PostAsJsonAsync(
            "/mfa/disable",
            new MfaVerifyRequestDto(code));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var statusResponse = await Client.GetAsync("/mfa/status");
        var status = await statusResponse.ToResponseModel<MfaStatusDto>();
        status.State.Should().Be(MfaState.PendingSetup);
        status.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task RegenerateRecoveryCodes_ReturnsNewSet()
    {
        var (manualKey, originalCodes) = await CompleteSetupWithCodesAsync();
        var code = GenerateTotpCode(manualKey);

        var response = await Client.PostAsJsonAsync(
            "/mfa/recovery-codes/regenerate",
            new MfaVerifyRequestDto(code));

        response.IsSuccessStatusCode.Should().BeTrue();
        var newCodes = await response.ToResponseModel<MfaRecoveryCodesDto>();
        newCodes.Codes.Should().HaveCount(10);
        newCodes.Codes.Should().NotIntersectWith(originalCodes);
    }

    private async Task<string> CompleteSetupAsync()
    {
        var (manualKey, _) = await CompleteSetupWithCodesAsync();
        return manualKey;
    }

    private async Task<(string ManualKey, List<string> RecoveryCodes)> CompleteSetupWithCodesAsync()
    {
        var setupResponse = await Client.PostAsync("/mfa/setup", content: null);
        var setup = await setupResponse.ToResponseModel<MfaSetupResponseDto>();
        var code = GenerateTotpCode(setup.ManualEntryKey);

        var verifyResponse = await Client.PostAsJsonAsync(
            "/mfa/setup/verify",
            new MfaVerifyRequestDto(code));
        var complete = await verifyResponse.ToResponseModel<MfaSetupCompleteResponseDto>();
        return (setup.ManualEntryKey, complete.RecoveryCodes);
    }

    private static string GenerateTotpCode(string base32Secret)
    {
        var secretBytes = Base32Encoding.ToBytes(base32Secret);
        var totp = new Totp(secretBytes, step: 30, totpSize: 6);
        return totp.ComputeTotp();
    }
}