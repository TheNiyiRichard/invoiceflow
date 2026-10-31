using System.Security.Claims;

namespace Invoiceflow.Api.Auth;

// Who is making this request, read from the sign-in cookie
public class CurrentUser(IHttpContextAccessor accessor)
{
    public string? Id => accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
}