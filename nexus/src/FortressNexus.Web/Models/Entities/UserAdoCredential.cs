namespace FortressNexus.Web.Models.Entities;

public class UserAdoCredential
{
    public int Id { get; set; }
    public string UserUpn { get; set; } = "";         // e.g. fwhite@fortressaffinitygroup.com
    public string EncryptedPat { get; set; } = "";    // AES encrypted via DataProtection
    public string PatHint { get; set; } = "";         // last 4 chars of raw PAT, for display
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
