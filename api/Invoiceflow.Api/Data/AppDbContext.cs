using Microsoft.EntityFrameworkCore;

namespace Invoiceflow.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
}