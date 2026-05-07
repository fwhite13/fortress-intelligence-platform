namespace FortressAI.V2.Web.Services.Exceptions;

public class ProvisioningException : Exception
{
    public string UserId { get; }
    public string FailedStep { get; }

    public ProvisioningException(string userId, string failedStep, string message, Exception? inner = null)
        : base(message, inner)
    {
        UserId = userId;
        FailedStep = failedStep;
    }
}
