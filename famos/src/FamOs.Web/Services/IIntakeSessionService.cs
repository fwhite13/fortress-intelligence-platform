namespace FamOs.Web.Services;

public interface IIntakeSessionService
{
    /// <summary>Creates or refreshes OTP session. Returns (sessionId, otpCode).</summary>
    Task<(long SessionId, string OtpCode)> CreateOrRefreshSessionAsync(string opportunityId, string email);

    /// <summary>Verifies OTP. Returns last_page on success, throws on failure.</summary>
    Task<string?> VerifyOtpAsync(long sessionId, string otpCode);

    /// <summary>Updates last_page for resume.</summary>
    Task UpdateLastPageAsync(long sessionId, string pageName);

    /// <summary>Marks session complete.</summary>
    Task CompleteSessionAsync(long sessionId);
}
