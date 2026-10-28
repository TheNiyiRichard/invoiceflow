using System.ComponentModel.DataAnnotations;

namespace Invoiceflow.Api.Dtos;

public class RegisterInput
{
    [Required, EmailAddress, StringLength(254)]
    public string Email { get; set; } = "";

    // Identity enforces the minimum length. The 128 cap stops huge strings being hashed.
    [Required, StringLength(128)]
    public string Password { get; set; } = "";
}
public class LoginInput
{
    [Required, EmailAddress, StringLength(254)]
    public string Email { get; set; } = "";

    [Required, StringLength(128)]
    public string Password { get; set; } = "";
}

public record MeDto(string Email);