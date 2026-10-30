using System.ComponentModel.DataAnnotations;

namespace Invoiceflow.Api.Dtos;

public class RegisterInput
{
    [Required(ErrorMessage = "Enter your name."), StringLength(100, ErrorMessage = "Keep your name under 100 characters.")]
    public string FullName { get; set; } = "";

    [StringLength(200, ErrorMessage = "Keep the business name under 200 characters.")]
    public string? BusinessName { get; set; }

    [Required(ErrorMessage = "Enter your email."), EmailAddress(ErrorMessage = "Enter a valid email address."), StringLength(254)]
    public string Email { get; set; } = "";

    // Identity enforces the minimum length. The 128 cap stops huge strings being hashed.
    [Required(ErrorMessage = "Enter a password."), StringLength(128)]
    public string Password { get; set; } = "";
}

public class LoginInput
{
    [Required, EmailAddress, StringLength(254)]
    public string Email { get; set; } = "";

    [Required, StringLength(128)]
    public string Password { get; set; } = "";
}

public record MeDto(string Email, string FullName);