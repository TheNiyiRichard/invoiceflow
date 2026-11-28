namespace Invoiceflow.Api.Models;

// One backup code for two-factor sign-in. Only its hash is saved, like a password.
public class RecoveryCode
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = "";
    public string Hash { get; set; } = "";
    public DateTime? UsedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}