using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace IFB.Server.Data;
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>();
        if (args.Contains("--sqlserver")) options.UseSqlServer("Server=localhost;Database=IFBConnect;Integrated Security=true;TrustServerCertificate=true");
        else options.UseSqlite("Data Source=ifb_connect.db");
        return new AppDbContext(options.Options);
    }
}
