using Microsoft.EntityFrameworkCore;
using Phylet.Data.Configuration;
using Phylet.Data.Library;

namespace Phylet.Data;

public sealed class PhyletDbContext(DbContextOptions<PhyletDbContext> options, RuntimeOptions runtimeOptions) : DbContext(options)
{
    public RuntimeOptions RuntimeOptions { get; } = runtimeOptions;

    public DbSet<DeviceConfigurationEntry> DeviceConfigurations => Set<DeviceConfigurationEntry>();
    public DbSet<ArtistEntity> Artists => Set<ArtistEntity>();
    public DbSet<AlbumEntity> Albums => Set<AlbumEntity>();
    public DbSet<TrackEntity> Tracks => Set<TrackEntity>();
    public DbSet<FolderEntity> Folders => Set<FolderEntity>();
    public DbSet<LibraryScanState> LibraryScanStates => Set<LibraryScanState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DeviceConfigurationEntry>(entity =>
        {
            entity.HasKey(configuration => configuration.Key);
            entity.Property(configuration => configuration.Key).HasMaxLength(128);
            entity.Property(configuration => configuration.Value).HasMaxLength(2048);
        });

        modelBuilder.Entity<ArtistEntity>(entity =>
        {
            entity.HasKey(artist => artist.Id);
            entity.Property(artist => artist.Name).HasMaxLength(512);
            entity.Property(artist => artist.NormalizedName).HasMaxLength(512);
            entity.HasIndex(artist => artist.NormalizedName).IsUnique();
        });

        modelBuilder.Entity<AlbumEntity>(entity =>
        {
            entity.HasKey(album => album.Id);
            entity.Property(album => album.Title).HasMaxLength(512);
            entity.Property(album => album.NormalizedTitle).HasMaxLength(512);
            entity.Property(album => album.AlbumPathKey).HasMaxLength(2048);
            entity.Property(album => album.CoverRelativePath).HasMaxLength(2048);
            entity.HasIndex(album => new { album.ArtistId, album.NormalizedTitle, album.AlbumPathKey }).IsUnique();
            entity.HasOne(album => album.Artist)
                .WithMany(artist => artist.Albums)
                .HasForeignKey(album => album.ArtistId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TrackEntity>(entity =>
        {
            entity.HasKey(track => track.Id);
            entity.Property(track => track.RelativePath).HasMaxLength(2048);
            entity.Property(track => track.FileName).HasMaxLength(512);
            entity.Property(track => track.Title).HasMaxLength(512);
            entity.Property(track => track.TrackArtistName).HasMaxLength(512);
            entity.Property(track => track.Format).HasMaxLength(64);
            entity.Property(track => track.MimeType).HasMaxLength(128);
            entity.HasIndex(track => track.RelativePath).IsUnique();
            entity.HasOne(track => track.Album)
                .WithMany(album => album.Tracks)
                .HasForeignKey(track => track.AlbumId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(track => track.Folder)
                .WithMany(folder => folder.Tracks)
                .HasForeignKey(track => track.FolderId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<FolderEntity>(entity =>
        {
            entity.HasKey(folder => folder.Id);
            entity.Property(folder => folder.RelativePath).HasMaxLength(2048);
            entity.Property(folder => folder.Name).HasMaxLength(512);
            entity.HasIndex(folder => folder.RelativePath).IsUnique();
            entity.HasOne(folder => folder.ParentFolder)
                .WithMany(folder => folder.ChildFolders)
                .HasForeignKey(folder => folder.ParentFolderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LibraryScanState>(entity =>
        {
            entity.HasKey(state => state.Id);
            entity.Property(state => state.LastError).HasMaxLength(2048);
        });
    }
}
