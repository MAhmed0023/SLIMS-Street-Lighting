using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Pera.Infrastructure.Persistence;

/// <summary>Lets `dotnet ef migrations add` build the model without starting the API or reaching a database.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
        => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer("Server=.\\SQLEXPRESS;Database=Pera;Trusted_Connection=True;TrustServerCertificate=True").Options);
}
