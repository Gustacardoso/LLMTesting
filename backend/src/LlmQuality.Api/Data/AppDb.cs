using LlmQuality.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace LlmQuality.Api.Data;

public class AppDb(DbContextOptions<AppDb> options) : DbContext(options)
{
    public DbSet<TestCase> TestCases => Set<TestCase>();
    public DbSet<TestRun> TestRuns => Set<TestRun>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentChunk> Chunks => Set<DocumentChunk>();
    public DbSet<RagRun> RagRuns => Set<RagRun>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<TestCase>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Category).HasMaxLength(100).IsRequired();
            e.Property(x => x.SecurityType).HasConversion<string>().HasMaxLength(30);
            e.HasIndex(x => x.Category);
        });

        b.Entity<TestRun>(e =>
        {
            e.HasOne(x => x.TestCase).WithMany(x => x.Runs).HasForeignKey(x => x.TestCaseId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Model).HasMaxLength(100);
            e.Property(x => x.CostUsd).HasPrecision(12, 6);
            e.HasIndex(x => x.ExecutedAt);
            e.HasIndex(x => x.Model);
        });

        b.Entity<Document>(e => e.Property(x => x.FileName).HasMaxLength(260));
        b.Entity<DocumentChunk>(e =>
        {
            e.HasOne(x => x.Document).WithMany(x => x.Chunks).HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Embedding).HasColumnType("double precision[]");
        });
        b.Entity<RagRun>(e => e.Property(x => x.CostUsd).HasPrecision(12, 6));
    }
}
