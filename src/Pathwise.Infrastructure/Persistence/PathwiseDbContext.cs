using Microsoft.EntityFrameworkCore;

namespace Pathwise.Infrastructure.Persistence;

public sealed class PathwiseDbContext(DbContextOptions<PathwiseDbContext> options) : DbContext(options)
{
    public DbSet<PlayerAccountEntity> PlayerAccounts => Set<PlayerAccountEntity>();
    public DbSet<StoredMatchEntity> StoredMatches => Set<StoredMatchEntity>();
    public DbSet<MatchPayloadEntity> MatchPayloads => Set<MatchPayloadEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var player = modelBuilder.Entity<PlayerAccountEntity>();
        player.ToTable("PlayerAccounts").HasKey(x => x.Puuid);
        player.Property(x => x.Puuid).HasMaxLength(128);
        player.Property(x => x.GameName).HasMaxLength(64);
        player.Property(x => x.TagLine).HasMaxLength(16);
        player.Property(x => x.ConfiguredGameName).HasMaxLength(64);
        player.Property(x => x.ConfiguredTagLine).HasMaxLength(16);
        player.Property(x => x.Platform).HasMaxLength(16);
        player.Property(x => x.Regional).HasMaxLength(16);

        var match = modelBuilder.Entity<StoredMatchEntity>();
        match.ToTable("StoredMatches").HasKey(x => x.MatchId);
        match.Property(x => x.MatchId).HasMaxLength(32);
        match.Property(x => x.PlayerPuuid).HasMaxLength(128);
        match.Property(x => x.ChampionName).HasMaxLength(64);
        match.Property(x => x.TeamPosition).HasMaxLength(16);
        match.HasOne(x => x.Player).WithMany(x => x.Matches).HasForeignKey(x => x.PlayerPuuid).OnDelete(DeleteBehavior.Restrict);
        match.HasIndex(x => new { x.PlayerPuuid, x.QueueId, x.PlayedAtUtc });

        var payload = modelBuilder.Entity<MatchPayloadEntity>();
        payload.ToTable("MatchPayloads").HasKey(x => new { x.MatchId, x.Kind });
        payload.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16);
        payload.Property(x => x.State).HasConversion<string>().HasMaxLength(16);
        payload.Property(x => x.EndpointVersion).HasMaxLength(16);
        payload.HasOne(x => x.Match).WithMany(x => x.Payloads).HasForeignKey(x => x.MatchId).OnDelete(DeleteBehavior.Cascade);
    }
}
