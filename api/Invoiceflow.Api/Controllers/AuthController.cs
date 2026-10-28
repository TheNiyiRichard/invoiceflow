using Invoiceflow.Api.Dtos;
using Invoiceflow.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Invoiceflow.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn) : ControllerBase
{
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterInput input)
    {
        var email = input.Email.Trim();
        var user = new ApplicationUser { UserName = email, Email = email };
        var result = await users.CreateAsync(user, input.Password);
        if (result.Succeeded) return StatusCode(StatusCodes.Status201Created);

        var emailRejected = false;
        foreach (var e in result.Errors)
        {
            if (e.Code.StartsWith("Password")) ModelState.AddModelError(nameof(input.Password), e.Description);
            else emailRejected = true;
        }
        if (emailRejected) ModelState.AddModelError(nameof(input.Email), "This email can't be used to register.");
        return ValidationProblem(ModelState);
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginInput input)
    {
        // lockoutOnFailure becomes true tomorrow (Day 11)
        var result = await signIn.PasswordSignInAsync(input.Email.Trim(), input.Password, isPersistent: false, lockoutOnFailure: false);
        return result.Succeeded
            ? NoContent()
            : Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid email or password.");
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await signIn.SignOutAsync();
        return NoContent();
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<MeDto>> Me()
    {
        var user = await users.GetUserAsync(User);
        return user is null ? Unauthorized() : new MeDto(user.Email!);
    }
}