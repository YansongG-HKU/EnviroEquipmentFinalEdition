using Microsoft.EntityFrameworkCore;
using SiemensS7Demo.Persistence.Entities;

namespace SiemensS7Demo.Persistence;

public sealed class EnviroDbContext : DbContext
{
    public EnviroDbContext(DbContextOptions<EnviroDbContext> options) : base(options) { }

    public DbSet<ProgramRow> Programs => Set<ProgramRow>();
    public DbSet<HistoryPointRow> HistoryPoints => Set<HistoryPointRow>();
    public DbSet<AlarmEventRow> AlarmEvents => Set<AlarmEventRow>();
    public DbSet<UserRow> Users => Set<UserRow>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        var p = b.Entity<ProgramRow>();
        p.ToTable("Programs");
        p.HasKey(x => x.Name);
        p.Property(x => x.Name).HasMaxLength(128);
        p.Property(x => x.JsonBlob).IsRequired();
        p.Property(x => x.UpdatedAt).IsRequired();

        var h = b.Entity<HistoryPointRow>();
        h.ToTable("HistoryPoints");
        h.HasKey(x => x.Id);
        h.Property(x => x.Id).ValueGeneratedOnAdd();
        h.Property(x => x.DeviceId).IsRequired().HasMaxLength(64);
        h.Property(x => x.At).IsRequired();
        h.HasIndex(x => new { x.DeviceId, x.At })
         .HasDatabaseName("IX_HistoryPoints_DeviceId_At");

        var a = b.Entity<AlarmEventRow>();
        a.ToTable("AlarmEvents");
        a.HasKey(x => x.Id);
        a.Property(x => x.Id).HasMaxLength(128);
        a.Property(x => x.DeviceId).IsRequired().HasMaxLength(64);
        a.Property(x => x.Code).IsRequired().HasMaxLength(64);
        a.Property(x => x.Message).IsRequired();
        a.HasIndex(x => new { x.DeviceId, x.At })
         .HasDatabaseName("IX_AlarmEvents_DeviceId_At");

        var u = b.Entity<UserRow>();
        u.ToTable("Users");
        u.HasKey(x => x.Id);
        u.Property(x => x.Id).HasMaxLength(64);
        u.Property(x => x.Name).IsRequired().HasMaxLength(128);
        u.Property(x => x.Code).IsRequired().HasMaxLength(64);
        u.Property(x => x.PasswordHash).IsRequired();
        u.HasIndex(x => x.Code).IsUnique();
    }
}
