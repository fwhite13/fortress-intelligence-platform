namespace FortressAI.V2.Web.Services;

public class FipTokenProvider : IFipTokenProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public FipTokenProvider(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Task<string?> GetAccessTokenAsync()
    {
        var ctx = _httpContextAccessor.HttpContext;
        if (ctx == null) return Task.FromResult<string?>(null);

        var token = ctx.User.FindFirst("access_token")?.Value
                 ?? ctx.User.FindFirst("token")?.Value;

        return Task.FromResult(token);
    }
}
