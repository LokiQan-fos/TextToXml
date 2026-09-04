using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Kape22Importer.Persistence;

// Database-first context over the AscoLSI target tables (AR-8). No migration is ever generated: the
// schema is owned by the database and mirrored for tests by scripts/schema/01-ascolsi-tables.sql.
// The connection string is supplied by configuration through AddAscoLsiPersistence (NFR-5, CC-7).
public class AscoLsiDbContext(DbContextOptions<AscoLsiDbContext> options) : DbContext(options)
{
    public DbSet<L_D_KAPE22> Kape22Rows => Set<L_D_KAPE22>();

    public DbSet<L_D_LOG_COMMANDE> LogCommandeRows => Set<L_D_LOG_COMMANDE>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<L_D_KAPE22>(entity =>
        {
            entity.ToTable("L_D_KAPE22");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).ValueGeneratedOnAdd();
        });

        modelBuilder.Entity<L_D_LOG_COMMANDE>(entity =>
        {
            entity.ToTable("L_D_LOG_COMMANDE");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).ValueGeneratedOnAdd();
        });

        // The real columns are legacy datetime, not datetime2. Pin the store type so EF stops emitting
        // datetime2 parameters and the scripts/schema parity test can lock it (AR-8, risk R-3, PRD D14).
        foreach (IMutableProperty property in modelBuilder.Model.GetEntityTypes()
            .SelectMany(type => type.GetProperties())
            .Where(property => property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?)))
        {
            property.SetColumnType("datetime");
        }
    }
}
