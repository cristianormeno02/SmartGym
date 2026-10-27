using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SmartGym.Infrastructure.Persistence;

public class SmartGymDbContextFactory : IDesignTimeDbContextFactory<SmartGymDbContext>
{
    public SmartGymDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<SmartGymDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Database=smartgym_design;Username=postgres;Password=postgres");

        return new SmartGymDbContext(optionsBuilder.Options);
    }
}
