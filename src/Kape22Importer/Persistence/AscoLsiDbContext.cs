using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Kape22Importer.Persistence;

// Database-first context over the AscoLSI target tables (AR-8). No migration is ever generated: the
// schema is owned by the database and mirrored for tests by scripts/schema/01-ascolsi-tables.sql.
// The consumer builds the context per Fichier with a UseSqlServer options object whose connection
// string comes from configuration, never a literal (NFR-5, CC-7).
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

            // The bounded string column lengths come from constants derived once from the real schema
            // (Annexe C), never a live sys.columns query. The startup compatibility check reads them
            // back through the model (AC-FR8-2, risk R-6).
            foreach ((string column, int maxLength) in Kape22ColumnLengths.MaxLengths)
            {
                entity.Property(column).HasMaxLength(maxLength);
            }
        });

        modelBuilder.Entity<L_D_LOG_COMMANDE>(entity =>
        {
            entity.ToTable("L_D_LOG_COMMANDE");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).ValueGeneratedOnAdd();

            // The bounded string column lengths mirror scripts/schema/01-ascolsi-tables.sql (Annexe
            // C.2), so an over-long value surfaces as a model error rather than only at the database.
            entity.Property(row => row.Commande).HasMaxLength(LogCommandeColumnLengths.Commande);
            entity.Property(row => row.OF).HasMaxLength(LogCommandeColumnLengths.OF);
            entity.Property(row => row.User).HasMaxLength(LogCommandeColumnLengths.User);
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
