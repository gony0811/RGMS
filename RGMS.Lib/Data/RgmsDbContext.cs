using Microsoft.EntityFrameworkCore;
using RGMS.Lib.Data.Entities;

namespace RGMS.Lib.Data;

public class RgmsDbContext : DbContext
{
    public RgmsDbContext(DbContextOptions<RgmsDbContext> options) : base(options)
    {
    }

    public DbSet<GeneralSettingsEntity> GeneralSettings => Set<GeneralSettingsEntity>();
    public DbSet<DaqChannelSettingEntity> DaqChannelSettings => Set<DaqChannelSettingEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var general = modelBuilder.Entity<GeneralSettingsEntity>();
        general.ToTable("GeneralSettings");
        general.HasKey(x => x.Id);
        general.Property(x => x.DeviceName).HasMaxLength(128);

        var channel = modelBuilder.Entity<DaqChannelSettingEntity>();
        channel.ToTable("DaqChannelSettings");
        channel.HasKey(x => x.Id);
        channel.Property(x => x.PhysicalChannel).HasMaxLength(128);
        channel.Property(x => x.Name).HasMaxLength(128);
        channel.Property(x => x.Terminal).HasConversion<int>();

        channel.HasOne(x => x.GeneralSettings)
            .WithMany(x => x.Channels)
            .HasForeignKey(x => x.GeneralSettingsId)
            .OnDelete(DeleteBehavior.Cascade);

        channel.HasIndex(x => new { x.GeneralSettingsId, x.ChannelIndex }).IsUnique();
    }
}
