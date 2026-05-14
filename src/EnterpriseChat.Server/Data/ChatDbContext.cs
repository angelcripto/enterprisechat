using EnterpriseChat.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseChat.Server.Data;

public sealed class ChatDbContext(DbContextOptions<ChatDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<RoomMember> RoomMembers => Set<RoomMember>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<LicenseRecord> Licenses => Set<LicenseRecord>();
    public DbSet<PinnedMessage> PinnedMessages => Set<PinnedMessage>();
    public DbSet<MessageReaction> MessageReactions => Set<MessageReaction>();
    public DbSet<SavedMessage> SavedMessages => Set<SavedMessage>();
    public DbSet<AuthProviderConfig> AuthProviders => Set<AuthProviderConfig>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(b =>
        {
            b.HasIndex(u => u.Username).IsUnique();
            b.Property(u => u.Role).HasConversion<int>();

            // El par (proveedor, externalId) identifica de forma única
            // a un usuario en su sistema de origen. Permite tener dos
            // usuarios distintos con el mismo externalId siempre que
            // provengan de proveedores diferentes.
            b.HasIndex(u => new { u.SourceProviderId, u.ExternalId })
                .IsUnique()
                .HasFilter("\"ExternalId\" IS NOT NULL");

            b.HasOne(u => u.SourceProvider)
                .WithMany()
                .HasForeignKey(u => u.SourceProviderId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AuthProviderConfig>(b =>
        {
            b.Property(p => p.Kind).HasConversion<int>();
            b.Property(p => p.HashAlgorithm).HasConversion<int>();
            b.HasIndex(p => p.Priority);
        });

        modelBuilder.Entity<Department>(b =>
        {
            b.HasIndex(d => d.Name).IsUnique();
        });

        modelBuilder.Entity<Session>(b =>
        {
            b.HasIndex(s => s.ConnectionId).IsUnique();
            b.HasIndex(s => new { s.UserId, s.DisconnectedAt });
        });

        modelBuilder.Entity<Message>(b =>
        {
            // Cursor pagination by (FromUserId, ToUserId, Id) and (RoomId, Id).
            b.HasIndex(m => new { m.FromUserId, m.ToUserId, m.Id });
            b.HasIndex(m => new { m.ToUserId, m.FromUserId, m.Id });
            b.HasIndex(m => new { m.RoomId, m.Id });

            // Avoid cascade chains in SQLite (no DELETE cascade on FromUser/ToUser).
            b.HasOne(m => m.FromUser)
                .WithMany()
                .HasForeignKey(m => m.FromUserId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(m => m.ToUser)
                .WithMany()
                .HasForeignKey(m => m.ToUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AuditLog>(b =>
        {
            b.HasIndex(a => a.Timestamp);
            b.HasIndex(a => a.Action);
        });

        modelBuilder.Entity<Room>(b =>
        {
            b.HasIndex(r => r.Name);
            b.HasOne(r => r.CreatedBy)
                .WithMany()
                .HasForeignKey(r => r.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RoomMember>(b =>
        {
            b.HasKey(rm => new { rm.RoomId, rm.UserId });
            b.HasIndex(rm => rm.UserId);
            b.HasOne(rm => rm.Room)
                .WithMany(r => r.Members)
                .HasForeignKey(rm => rm.RoomId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(rm => rm.User)
                .WithMany()
                .HasForeignKey(rm => rm.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Attachment>(b =>
        {
            b.HasIndex(a => a.UploadedByUserId);
            b.HasOne(a => a.UploadedBy)
                .WithMany()
                .HasForeignKey(a => a.UploadedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Message>(b =>
        {
            b.HasOne(m => m.Attachment)
                .WithMany()
                .HasForeignKey(m => m.AttachmentId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<LicenseRecord>(b =>
        {
            b.HasIndex(l => l.Status);
            b.HasIndex(l => l.Jti).IsUnique();
            b.HasOne(l => l.AppliedBy)
                .WithMany()
                .HasForeignKey(l => l.AppliedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<PinnedMessage>(b =>
        {
            b.HasKey(p => new { p.RoomId, p.MessageId });
            b.HasIndex(p => p.RoomId);
            b.HasOne(p => p.Room)
                .WithMany()
                .HasForeignKey(p => p.RoomId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(p => p.Message)
                .WithMany()
                .HasForeignKey(p => p.MessageId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(p => p.PinnedBy)
                .WithMany()
                .HasForeignKey(p => p.PinnedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<MessageReaction>(b =>
        {
            b.HasKey(r => new { r.MessageId, r.UserId, r.Emoji });
            b.HasIndex(r => r.MessageId);
            b.HasOne(r => r.Message)
                .WithMany()
                .HasForeignKey(r => r.MessageId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(r => r.User)
                .WithMany()
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SavedMessage>(b =>
        {
            b.HasKey(s => new { s.UserId, s.MessageId });
            b.HasIndex(s => s.UserId);
            b.HasOne(s => s.Message)
                .WithMany()
                .HasForeignKey(s => s.MessageId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(s => s.User)
                .WithMany()
                .HasForeignKey(s => s.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
