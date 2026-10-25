using System.ComponentModel.DataAnnotations;

namespace Invoiceflow.Api.Dtos;

// What the API sends back
public record ClientDto(Guid Id, string Name, string Email, string? Company, string? Address, DateTime CreatedAt);

// What the API accepts. No Id or CreatedAt, so callers can't set them.
public class ClientInput
{
    [Required, StringLength(200)]
    public string Name { get; set; } = "";

    [Required, EmailAddress, StringLength(254)]
    public string Email { get; set; } = "";

    [StringLength(200)]
    public string? Company { get; set; }

    [StringLength(500)]
    public string? Address { get; set; }
}