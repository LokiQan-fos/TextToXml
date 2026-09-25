using Microsoft.EntityFrameworkCore;

namespace AscoLsiJournal;

// Database-first context over the single AscoLSI table the journal writes, L_D_LOG_COMMANDE (AD-5, no
// migration). The bounded string lengths mirror scripts/schema/01-ascolsi-tables.sql so the model
// matches the table; EF Core does not check them at SaveChanges, the database does.
public class AscoLsiJournalDbContext(DbContextOptions<AscoLsiJournalDbContext> options) : DbContext(options)
{
    public DbSet<L_D_LOG_COMMANDE> LogCommandeRows => this.Set<L_D_LOG_COMMANDE>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<L_D_LOG_COMMANDE>(entity =>
        {
            entity.ToTable("L_D_LOG_COMMANDE");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).ValueGeneratedOnAdd();
            entity.Property(row => row.Commande).HasMaxLength(LogCommandeColumnLengths.Commande);
            entity.Property(row => row.OF).HasMaxLength(LogCommandeColumnLengths.OF);
            entity.Property(row => row.User).HasMaxLength(LogCommandeColumnLengths.User);
        });
    }
}
