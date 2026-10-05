using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kape22Importer.Persistence;

// Database-first context over the AscoLSI target tables (AR-8). No migration is ever generated: the
// schema is owned by the database and mirrored for tests by scripts/schema/01-ascolsi-tables.sql.
// The consumer builds the context per Fichier with a UseSqlServer options object whose connection
// string comes from configuration, never a literal (NFR-5, CC-7).
public class AscoLsiDbContext(DbContextOptions<AscoLsiDbContext> options) : DbContext(options)
{
    public DbSet<L_D_CONSIGNES> ConsignesRows => Set<L_D_CONSIGNES>();

    public DbSet<L_D_COULEE> CouleeRows => Set<L_D_COULEE>();

    // Story 6.10 (D34): read-only precondition table, never written.
    public DbSet<L_D_FOURS> FoursRows => Set<L_D_FOURS>();

    public DbSet<L_D_KAPE22> Kape22Rows => Set<L_D_KAPE22>();

    // Story 6.10 review (P-9): only deleted from, by the D34 replace.
    public DbSet<L_D_MAM_QUAL> MamQualRows => Set<L_D_MAM_QUAL>();

    // Story 6.10 review (P-9): only deleted from, by the D34 replace.
    public DbSet<L_D_OF_SUIVI> OfSuiviRows => Set<L_D_OF_SUIVI>();

    public DbSet<L_D_ORDRE_FABRICATION> OrdreFabricationRows => Set<L_D_ORDRE_FABRICATION>();

    // Story 6.10 (D34): read-only precondition table, never written.
    public DbSet<L_D_PLANS_FOURS> PlansFoursRows => Set<L_D_PLANS_FOURS>();

    // Story 6.10 review (P-9): only deleted from, by the D34 replace.
    public DbSet<L_D_PRODUITS_OUTIL> ProduitsOutilRows => Set<L_D_PRODUITS_OUTIL>();

    // Story 6.10 (D34): read-only precondition table, never written.
    public DbSet<L_D_PSO> PsoRows => Set<L_D_PSO>();

    // Story 6.10 review (P-9): only deleted from, by the D34 replace.
    public DbSet<L_D_REBUT> RebutRows => Set<L_D_REBUT>();

    public DbSet<L_D_SECTIONCHARGE_CHUTAGE> SectionChargeChutageRows => Set<L_D_SECTIONCHARGE_CHUTAGE>();

    public DbSet<L_D_SECTIONCHARGE_DECOUPE> SectionChargeDecoupeRows => Set<L_D_SECTIONCHARGE_DECOUPE>();

    public DbSet<L_D_SECTIONCHARGE_LINGOT> SectionChargeLingotRows => Set<L_D_SECTIONCHARGE_LINGOT>();

    public DbSet<L_D_SECTIONCHARGE_PITS> SectionChargePitsRows => Set<L_D_SECTIONCHARGE_PITS>();

    public DbSet<L_D_SECTIONCHARGE_POIDSMETRIQUE> SectionChargePoidsMetriqueRows => Set<L_D_SECTIONCHARGE_POIDSMETRIQUE>();

    public DbSet<L_D_SECTIONCHARGE_REFROIDISSOIRS> SectionChargeRefroidissoirsRows => Set<L_D_SECTIONCHARGE_REFROIDISSOIRS>();

    public DbSet<L_D_SECTIONCHARGE_SVT> SectionChargeSvtRows => Set<L_D_SECTIONCHARGE_SVT>();

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

        // Story 4.1: the 10 downstream dispatch tables. None has an identity column (AFV004-LSI
        // sys.columns, 2026-09-14); every key below is the real natural, composite business key
        // (sys.indexes), so no ValueGeneratedOnAdd here.
        modelBuilder.Entity<L_D_ORDRE_FABRICATION>(entity =>
        {
            entity.ToTable("L_D_ORDRE_FABRICATION");
            entity.HasKey(row => row.OF);
            ApplyDownstreamColumnLengths(entity);
            ApplyDownstreamColumnPrecisions(entity);
        });

        modelBuilder.Entity<L_D_COULEE>(entity =>
        {
            entity.ToTable("L_D_COULEE");
            entity.HasKey(row => row.IdCoulee);
            ApplyDownstreamColumnLengths(entity);
            ApplyDownstreamColumnPrecisions(entity);
        });

        modelBuilder.Entity<L_D_CONSIGNES>(entity =>
        {
            entity.ToTable("L_D_CONSIGNES");
            entity.HasKey(row => new { row.OF, row.CodeOperation, row.TypeConsigne, row.ConsigneGPAO });
            ApplyDownstreamColumnLengths(entity);
        });

        modelBuilder.Entity<L_D_SECTIONCHARGE_CHUTAGE>(entity =>
        {
            entity.ToTable("L_D_SECTIONCHARGE_CHUTAGE");
            entity.HasKey(row => new { row.OF, row.CodeOperation });
            ApplyDownstreamColumnLengths(entity);
            ApplyDownstreamColumnPrecisions(entity);
        });

        modelBuilder.Entity<L_D_SECTIONCHARGE_DECOUPE>(entity =>
        {
            entity.ToTable("L_D_SECTIONCHARGE_DECOUPE");
            entity.HasKey(row => new { row.OF, row.CodeOperation });
            ApplyDownstreamColumnLengths(entity);
            ApplyDownstreamColumnPrecisions(entity);
        });

        modelBuilder.Entity<L_D_SECTIONCHARGE_LINGOT>(entity =>
        {
            entity.ToTable("L_D_SECTIONCHARGE_LINGOT");
            entity.HasKey(row => new { row.OF, row.CodeOperation });
            ApplyDownstreamColumnLengths(entity);
            ApplyDownstreamColumnPrecisions(entity);
        });

        modelBuilder.Entity<L_D_SECTIONCHARGE_PITS>(entity =>
        {
            entity.ToTable("L_D_SECTIONCHARGE_PITS");
            entity.HasKey(row => new { row.OF, row.CodeOperation });
            ApplyDownstreamColumnLengths(entity);
            ApplyDownstreamColumnPrecisions(entity);
        });

        modelBuilder.Entity<L_D_SECTIONCHARGE_POIDSMETRIQUE>(entity =>
        {
            entity.ToTable("L_D_SECTIONCHARGE_POIDSMETRIQUE");
            entity.HasKey(row => new { row.OF, row.CodeOperation });
            ApplyDownstreamColumnLengths(entity);
        });

        modelBuilder.Entity<L_D_SECTIONCHARGE_REFROIDISSOIRS>(entity =>
        {
            entity.ToTable("L_D_SECTIONCHARGE_REFROIDISSOIRS");
            entity.HasKey(row => new { row.OF, row.CodeOperation });
            ApplyDownstreamColumnLengths(entity);
        });

        modelBuilder.Entity<L_D_SECTIONCHARGE_SVT>(entity =>
        {
            entity.ToTable("L_D_SECTIONCHARGE_SVT");
            entity.HasKey(row => new { row.OF, row.CodeOperation });
            ApplyDownstreamColumnLengths(entity);
        });

        // Story 6.10 (D34): the 3 read-only precondition tables a re-sent OF is checked against (AFV004-LSI
        // sys.columns/sys.indexes, 2026-10-05). Minimal mappings, key + OF only; Kape22Persister only
        // queries them.
        modelBuilder.Entity<L_D_FOURS>(entity =>
        {
            entity.ToTable("L_D_FOURS");
            entity.HasKey(row => row.Id);
        });

        modelBuilder.Entity<L_D_PLANS_FOURS>(entity =>
        {
            entity.ToTable("L_D_PLANS_FOURS");
            entity.HasKey(row => new { row.FourId, row.Position });
        });

        modelBuilder.Entity<L_D_PSO>(entity =>
        {
            entity.ToTable("L_D_PSO");
            entity.HasKey(row => new { row.NumeroLingot, row.Coulee });
        });

        // Story 6.10 review (D-1/P-9): the 4 tables outside the 9 that the D34 replace deletes from, as the
        // legacy DeleteOF does (AFV004-LSI sys.columns/sys.indexes, 2026-10-05). Minimal mappings.
        modelBuilder.Entity<L_D_MAM_QUAL>(entity =>
        {
            entity.ToTable("L_D_MAM_QUAL");
            entity.HasKey(row => row.OF);
        });

        modelBuilder.Entity<L_D_OF_SUIVI>(entity =>
        {
            entity.ToTable("L_D_OF_SUIVI");
            entity.HasKey(row => row.OF);
        });

        modelBuilder.Entity<L_D_PRODUITS_OUTIL>(entity =>
        {
            entity.ToTable("L_D_PRODUITS_OUTIL");
            entity.HasKey(row => new { row.Zone, row.OF });
        });

        modelBuilder.Entity<L_D_REBUT>(entity =>
        {
            entity.ToTable("L_D_REBUT");
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

    // Story 4.1: applies the DownstreamColumnLengths entries that exist on this particular entity (the
    // map is shared across the 10 downstream tables; most tables only use a subset of its columns).
    private static void ApplyDownstreamColumnLengths<TEntity>(EntityTypeBuilder<TEntity> entity)
        where TEntity : class
    {
        foreach ((string column, int maxLength) in DownstreamColumnLengths.MaxLengths)
        {
            if (entity.Metadata.FindProperty(column) is not null)
            {
                entity.Property(column).HasMaxLength(maxLength);
            }
        }
    }

    // Without an explicit precision/scale, EF Core falls back to decimal(18,2) for every unconfigured
    // decimal property - silently rounding a scale-3 column (PoidsDemiProduitUnitaire, LongueurCD, ...)
    // to 2 decimals in the outbound SqlParameter before it ever reaches the real, wider column. Same
    // shared-map-restricted-to-this-entity pattern as ApplyDownstreamColumnLengths above.
    private static void ApplyDownstreamColumnPrecisions<TEntity>(EntityTypeBuilder<TEntity> entity)
        where TEntity : class
    {
        foreach ((string column, (int precision, int scale)) in DownstreamColumnPrecisions.Precisions)
        {
            if (entity.Metadata.FindProperty(column) is not null)
            {
                entity.Property(column).HasPrecision(precision, scale);
            }
        }
    }
}
