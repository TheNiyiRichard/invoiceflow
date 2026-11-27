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

public record MeDto(string Email, string FullName, string? BusinessName, bool EmailConfirmed, bool TwoFactorEnabled);

public class VerifyEmailInput
{
    [Required, StringLength(64)]
    public string UserId { get; set; } = "";

    [Required, StringLength(1024)]
    public string Code { get; set; } = "";
}

public class ForgotPasswordInput
{
    [Required(ErrorMessage = "Enter your email."), EmailAddress(ErrorMessage = "Enter a valid email address."), StringLength(254)]
    public string Email { get; set; } = "";
}

public class ResetPasswordInput
{
    [Required, StringLength(64)]
    public string UserId { get; set; } = "";

    [Required, StringLength(1024)]
    public string Code { get; set; } = "";

    // Same rules as sign-up (Identity checks them). The 128 cap stops huge strings being hashed.
    [Required(ErrorMessage = "Enter a new password."), StringLength(128)]
    public string Password { get; set; } = "";
}

public class TwoFactorSetupInput
{
    [Required(ErrorMessage = "Enter your password."), StringLength(128)]
    public string Password { get; set; } = "";
}

public class TwoFactorCodeInput
{
    // 6 digits. Spaces and dashes are allowed, because apps show "123 456".
    [Required(ErrorMessage = "Enter the 6-digit code from your app."),
     RegularExpression(@"^[0-9 \-]{6,9}$", ErrorMessage = "Enter the 6-digit code from your app.")]
    public string Code { get; set; } = "";
}

// The secret, shown once while setting up: as a QR code (the URI) and as text to type in
public record TwoFactorSetupDto(string SharedKey, string AuthenticatorUri);