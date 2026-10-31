namespace Invoiceflow.Api.Models;

public class Client
{
    public Guid Id { get; set; }

    // Set by the server from the sign-in cookie. Never part of ClientInput.
    public string OwnerId { get; set; } = "";

    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string? Company { get; set; }
    public string? Address { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}