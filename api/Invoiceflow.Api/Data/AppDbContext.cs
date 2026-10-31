using Invoiceflow.Api.Auth;
using Invoiceflow.Api.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Invoiceflow.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options, CurrentUser currentUser) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Client> Clients => Set<Client>();

    // Read fresh on every query, so each request only sees its own user's rows
    string? CurrentUserId => currentUser.Id;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder); // builds the Identity tables, must come first

        modelBuilder.Entity<ApplicationUser>(u =>
        {
            u.HasIndex(x => x.NormalizedEmail).IsUnique().HasDatabaseName("EmailIndex");
            u.Property(x => x.FullName).HasMaxLength(100).IsRequired();
            u.Property(x => x.BusinessName).HasMaxLength(200);
        });

        modelBuilder.Entity<Client>(c =>
        {
            c.Property(x => x.Name).HasMaxLength(200).IsRequired();
            c.Property(x => x.Email).HasMaxLength(254).IsRequired();
            c.Property(x => x.Company).HasMaxLength(200);
            c.Property(x => x.Address).HasMaxLength(500);

            c.HasOne<ApplicationUser>().WithMany()
                .HasForeignKey(x => x.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);

            // Safety net: even a query that forgets to filter only sees the signed-in user's clients
            c.HasQueryFilter(x => x.OwnerId == CurrentUserId);
        });
    }
}