using Microsoft.AspNetCore.Identity;

namespace Invoiceflow.Api.Models;

public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = "";
    public string? BusinessName { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    // The authenticator time step of the last code accepted, so each code works only once
    public long? LastTotpStep { get; set; }
}