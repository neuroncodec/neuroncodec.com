using Microsoft.EntityFrameworkCore;
using NeuronCodec.Web.Data.Entities;

namespace NeuronCodec.Web.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Article> Articles => Set<Article>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<ArticleTag> ArticleTags => Set<ArticleTag>();
    public DbSet<MediaFile> MediaFiles => Set<MediaFile>();
    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();
    public DbSet<RecoveryCode> RecoveryCodes => Set<RecoveryCode>();
    public DbSet<SiteSetting> SiteSettings => Set<SiteSetting>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Article>(e =>
        {
            e.HasIndex(a => a.Slug).IsUnique();
            e.Property(a => a.Slug).HasMaxLength(200).IsRequired();
            e.Property(a => a.Title).HasMaxLength(300).IsRequired();
            e.Property(a => a.Excerpt).HasMaxLength(1000);
            e.Property(a => a.TwitterCardType).HasMaxLength(50);
            e.Property(a => a.Status).HasConversion<int>();

            e.HasOne(a => a.Category)
                .WithMany(c => c.Articles)
                .HasForeignKey(a => a.CategoryId)
                .OnDelete(DeleteBehavior.SetNull);

            // Media referenced by an article is never cascade-deleted; the media screen blocks
            // deleting a file that is still in use.
            e.HasOne(a => a.FeaturedMedia)
                .WithMany()
                .HasForeignKey(a => a.FeaturedMediaId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasOne(a => a.OgImage)
                .WithMany()
                .HasForeignKey(a => a.OgImageId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasIndex(a => new { a.Status, a.PublishedAt });
        });

        b.Entity<Category>(e =>
        {
            e.HasIndex(c => c.Slug).IsUnique();
            e.Property(c => c.Name).HasMaxLength(120).IsRequired();
            e.Property(c => c.Slug).HasMaxLength(140).IsRequired();
            e.Property(c => c.TagClass).HasMaxLength(40).IsRequired();
        });

        b.Entity<Tag>(e =>
        {
            e.HasIndex(t => t.Slug).IsUnique();
            e.Property(t => t.Name).HasMaxLength(120).IsRequired();
            e.Property(t => t.Slug).HasMaxLength(140).IsRequired();
        });

        b.Entity<ArticleTag>(e =>
        {
            e.HasKey(at => new { at.ArticleId, at.TagId });
            e.HasOne(at => at.Article).WithMany(a => a.ArticleTags)
                .HasForeignKey(at => at.ArticleId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(at => at.Tag).WithMany(t => t.ArticleTags)
                .HasForeignKey(at => at.TagId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<MediaFile>(e =>
        {
            e.HasIndex(m => m.StoredName).IsUnique();
            e.Property(m => m.StoredName).HasMaxLength(260).IsRequired();
            e.Property(m => m.OriginalName).HasMaxLength(260).IsRequired();
            e.Property(m => m.ContentType).HasMaxLength(120).IsRequired();
            e.Property(m => m.AltText).HasMaxLength(400);
        });

        b.Entity<AdminUser>(e =>
        {
            e.HasIndex(u => u.Username).IsUnique();
            e.Property(u => u.Username).HasMaxLength(120).IsRequired();
            e.Property(u => u.PasswordHash).IsRequired();
        });

        b.Entity<RecoveryCode>(e =>
        {
            e.HasOne(r => r.AdminUser).WithMany(u => u.RecoveryCodes)
                .HasForeignKey(r => r.AdminUserId).OnDelete(DeleteBehavior.Cascade);
            e.Property(r => r.CodeHash).IsRequired();
        });

        b.Entity<SiteSetting>(e =>
        {
            e.HasKey(s => s.Key);
            e.Property(s => s.Key).HasMaxLength(120);
        });
    }
}
