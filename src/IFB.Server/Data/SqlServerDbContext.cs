using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
namespace IFB.Server.Data;
public sealed class SqlServerDbContext(DbContextOptions<SqlServerDbContext> options) : AppDbContext(options);
public sealed class SqlServerDesignTimeFactory : IDesignTimeDbContextFactory<SqlServerDbContext>
{
    public SqlServerDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<SqlServerDbContext>()
        .UseSqlServer("Server=localhost;Database=IFBConnect;Integrated Security=true;TrustServerCertificate=true").Options);
}
