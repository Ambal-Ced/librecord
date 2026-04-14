using LibRecord.Models;
using Microsoft.EntityFrameworkCore;

namespace LibRecord.Data;

public sealed class AppDbContext : DbContext
{
  public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

  public DbSet<Book> Books => Set<Book>();
  public DbSet<FieldDefinition> FieldDefinitions => Set<FieldDefinition>();
  public DbSet<BookFieldValue> BookFieldValues => Set<BookFieldValue>();

  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
    base.OnModelCreating(modelBuilder);

    modelBuilder.Entity<Book>(entity =>
    {
      entity.HasKey(x => x.Id);
      entity.Property(x => x.CreatedAt).IsRequired();
      entity.Property(x => x.UpdatedAt).IsRequired();
    });

    modelBuilder.Entity<FieldDefinition>(entity =>
    {
      entity.HasKey(x => x.Id);
      entity.Property(x => x.Name).IsRequired();
      entity.HasIndex(x => x.Name).IsUnique();
      entity.Property(x => x.Type).IsRequired();
      entity.Property(x => x.SortOrder).IsRequired();
    });

    modelBuilder.Entity<BookFieldValue>(entity =>
    {
      entity.HasKey(x => x.Id);

      entity.HasOne(x => x.Book)
        .WithMany(x => x.FieldValues)
        .HasForeignKey(x => x.BookId)
        .OnDelete(DeleteBehavior.Cascade);

      entity.HasOne(x => x.FieldDefinition)
        .WithMany()
        .HasForeignKey(x => x.FieldDefinitionId)
        .OnDelete(DeleteBehavior.Restrict);

      entity.HasIndex(x => new { x.BookId, x.FieldDefinitionId }).IsUnique();
      entity.HasIndex(x => x.FieldDefinitionId);
    });
  }
}

