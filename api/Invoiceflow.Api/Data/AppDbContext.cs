using Invoiceflow.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Invoiceflow.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Client> Clients => Set<Client>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Client>(c =>
        {
            c.Property(x => x.Name).HasMaxLength(200).IsRequired();
            c.Property(x => x.Email).HasMaxLength(254).IsRequired();
            c.Property(x => x.Company).HasMaxLength(200);
            c.Property(x => x.Address).HasMaxLength(500);

            var seeded = new DateTime(2026, 10, 23, 0, 0, 0, DateTimeKind.Utc);
            c.HasData(
                new Client { Id = Guid.Parse("3f1c2a9e-6b4d-4e8a-9c71-2d5e8f0a1b01"), Name = "Amara Okafor", Email = "amara@example.com", Company = "Okafor Studio", Address = "12 Allen Avenue, Ikeja, Lagos", CreatedAt = seeded },
                new Client { Id = Guid.Parse("8a7e5d21-0c3b-4f69-b2a4-71e9c6d3f802"), Name = "Daniel Mensah", Email = "daniel@example.com", Company = "Mensah Logistics", Address = "4 Oxford Street, Accra", CreatedAt = seeded },
                new Client { Id = Guid.Parse("c94b0f37-2e18-4d5a-8f60-e3a1b7d49c03"), Name = "Sarah Whitfield", Email = "sarah@example.com", Company = null, Address = "27 Market Lane, Leeds", CreatedAt = seeded }
            );
        });
    }
}