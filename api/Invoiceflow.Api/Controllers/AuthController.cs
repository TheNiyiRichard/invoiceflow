using Invoiceflow.Api.Dtos;
using Invoiceflow.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Invoiceflow.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(UserManager<ApplicationUser> users) : ControllerBase
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
}