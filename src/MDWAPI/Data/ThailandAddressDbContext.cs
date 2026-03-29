using MDWAPI.Entities;
using Microsoft.EntityFrameworkCore;

namespace MDWAPI.Data;

public class ThailandAddressDbContext : DbContext
{
    public ThailandAddressDbContext(DbContextOptions<ThailandAddressDbContext> options) : base(options)
    {
    }

    public DbSet<ThailandAddress> ThailandAddresses { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ThailandAddress>().HasIndex(x => x.province);
        modelBuilder.Entity<ThailandAddress>().HasIndex(x => x.district);
    }
}
