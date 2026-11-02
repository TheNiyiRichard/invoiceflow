using Invoiceflow.Api.Auth;
using Invoiceflow.Api.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Invoiceflow.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options, CurrentUser currentUser) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<LineItem> LineItems => Set<LineItem>();

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

        modelBuilder.Entity<Invoice>(i =>
        {
            // Stored as text ("Draft", "Paid"), so reordering the enum can never change saved invoices
            i.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            i.Property(x => x.Notes).HasMaxLength(2000);
            i.Property(x => x.Subtotal).HasPrecision(18, 2);
            i.Property(x => x.Tax).HasPrecision(18, 2);
            i.Property(x => x.Total).HasPrecision(18, 2);

            i.HasOne<ApplicationUser>().WithMany()
                .HasForeignKey(x => x.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);

            // A client that has invoices can't be deleted by accident
            i.HasOne(x => x.Client).WithMany()
                .HasForeignKey(x => x.ClientId)
                .OnDelete(DeleteBehavior.Restrict);

            i.HasIndex(x => new { x.OwnerId, x.Status }); // the invoices list filters by status

            // Same safety net as clients, from day one
            i.HasQueryFilter(x => x.OwnerId == CurrentUserId);
        });

        modelBuilder.Entity<LineItem>(li =>
        {
            li.Property(x => x.Description).HasMaxLength(500).IsRequired();
            li.Property(x => x.Quantity).HasPrecision(9, 2);
            li.Property(x => x.UnitPrice).HasPrecision(18, 2);

            li.HasOne(x => x.Invoice).WithMany(x => x.LineItems)
                .HasForeignKey(x => x.InvoiceId)
                .OnDelete(DeleteBehavior.Cascade);

            // Line items have no owner column, so they follow their invoice's owner
            li.HasQueryFilter(x => x.Invoice.OwnerId == CurrentUserId);
        });
    }
}