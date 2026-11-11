using CustomerCorrespondence.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace CustomerCorrespondence.Infrastructure.Persistence
{
    public class CorrespondenceDBContext : DbContext
    {
        public CorrespondenceDBContext(DbContextOptions<CorrespondenceDBContext> options) : base(options) { }
        public DbSet<CorrespondenceRequest> CorrespondenceRequests { get; set; } = null!;
        public DbSet<CorrespondenceRequestStatusHistory> CorrespondenceRequestStatusHistories { get; set; } = null!;
        public DbSet<CorrespondenceStatusCode> CorrespondenceStatusCodes { get; set; } = null!;
        public DbSet<RoutingConfiguration> RoutingConfigurations { get; set; } = null!;
        public DbSet<NonRecoverableErrorLog> NonRecoverableErrorLogs { get; set; } = null!;
        public DbSet<CorrespondenceDeliveryMethod> CorrespondenceDeliveryMethods { get; set; } = null!;
        public DbSet<DocumentIdEntity> DocumentIds { get; set; } = default!;
        public DbSet<CorrespondenceDocumentType> CorrespondenceDocumentTypes { get; set; } = null!;
        public DbSet<ChatConversationEntity> ChatConversations { get; set; } = null!;
        public DbSet<ChatMessageEntity> ChatMessages { get; set; } = null!;
        public DbSet<AiUsageLogEntity> AiUsageLogs { get; set; } = null!;
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            var jsonOptions = new System.Text.Json.JsonSerializerOptions();
            // Configure CorrespondenceRequest entity
            modelBuilder.Entity<CorrespondenceRequest>(entity =>
            {
                entity.ToTable("CorrespondenceRequest"); // Map to table
                entity.HasKey(e => e.CorrespondenceId); // Primary key
                entity.Property(e => e.DeliveryMethodCode).IsRequired().HasMaxLength(50);
                entity.Property(e => e.RequestCreationTS).HasDefaultValueSql("GETUTCDATE()");
                entity.Property(e => e.DocId).HasMaxLength(19);
                entity.Property(e => e.RequestingDomainName).HasMaxLength(100);
                // Optional string properties with max length constraints
                entity.Property(e => e.VersionCode).HasMaxLength(50);
                entity.Property(e => e.ArchiveDocumentTypeCode).HasMaxLength(50);
                entity.Property(e => e.ArchiveDocumentSubTypeCode).HasMaxLength(50);
                entity.Property(e => e.PolicyNumber).HasColumnType("varbinary(max)");
                entity.Property(e => e.ClaimNumber).HasMaxLength(50);
                entity.Property(e => e.CustomerId).HasMaxLength(50);
                entity.Property(e => e.RequestPayload).HasColumnType("varbinary(max)"); // For byte array
                // Define the one-to-many relationship
                entity.HasMany<CorrespondenceRequestStatusHistory>(e => e.StatusHistories)
                      .WithOne()
                      .HasForeignKey(e => e.CorrespondenceId)
                      .OnDelete(DeleteBehavior.Cascade); // Cascade delete
            });
            // Configure CorrespondenceRequestStatusHistory entity
            modelBuilder.Entity<CorrespondenceRequestStatusHistory>(entity =>
            {
                entity.ToTable("CorrespondenceRequestStatusHistory"); // Map to table
                entity.HasKey(e => new { e.CorrespondenceRequestStatusHistoryId, e.HistorySequenceNum }); // Primary key
                entity.Property(e => e.StatusCode).IsRequired().HasMaxLength(50);
                entity.Property(e => e.StatusUpdateTS).HasDefaultValueSql("GETUTCDATE()");
                entity.Property(e => e.HistorySequenceNum).IsRequired();
                entity.Property(e => e.StatusUpdatedBy).IsRequired().HasMaxLength(100);
            });
            modelBuilder.Entity<CorrespondenceStatusCode>(entity =>
            {
                entity.ToTable("CorrespondenceStatusCode"); // Map to table
                entity.HasKey(e => e.StatusId); // Primary key
                entity.Property(e => e.StatusCode).IsRequired().HasMaxLength(50);
                entity.Property(e => e.StatusDescription).IsRequired().HasMaxLength(100);
            });

            // Configure RoutingConfiguration entity - SIMPLIFIED VERSION
            modelBuilder.Entity<RoutingConfiguration>(entity =>
            {
                entity.ToTable("RoutingConfigurations");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.RouteName).HasMaxLength(100);
                entity.Property(e => e.CustomerName).HasMaxLength(100);
                entity.Property(e => e.CorrespondenceMethod).HasMaxLength(50);

                // Map RouteData property to the Route column in database
                entity.Property(e => e.RouteData)
                      .HasColumnName("Route")
                      .HasMaxLength(4000)
                      .IsRequired();
                // Don't map the computed Route property to database
                entity.Ignore(e => e.Route);
                // Create index
                entity.HasIndex(e => e.CustomerName).HasDatabaseName("IX_RoutingConfigurations_CustomerName");
            });

            // Configure CorrespondenceDeliveryMethod entity
            modelBuilder.Entity<CorrespondenceDeliveryMethod>(entity =>
            {
                entity.ToTable("CorrespondenceDeliveryMethod"); // Map to table
                entity.HasKey(e => e.DeliveryMethodId); // Primary key
                entity.Property(e => e.DeliveryMethodCode).IsRequired().HasMaxLength(50);
                entity.Property(e => e.DeliveryMethodName).IsRequired().HasMaxLength(100);
            });
            // Configure CorrespondenceDeliveryMethod entity
            modelBuilder.Entity<DocumentIdEntity>(entity =>
            {
                entity.ToTable("DocumentIds"); // Map to table
                entity.HasKey(e => e.SeqId); // Primary key
                entity.Property(e => e.DocIds).IsRequired().HasMaxLength(19);
                entity.Property(e => e.DocIdStatus).IsRequired().HasMaxLength(20);
            });

            modelBuilder.Entity<CorrespondenceDocumentType>(entity =>
            {
                entity.ToTable("CorrespondenceDocumentType"); // Map to table with schema
                entity.HasKey(e => e.CorrespondenceDocumentTypeId); // Primary key
                entity.Property(e => e.DocumentType).IsRequired().HasMaxLength(100);
                entity.Property(e => e.DocumentSubType).HasMaxLength(100);
                entity.Property(e => e.CabinetName).HasMaxLength(100);
                // Optional: Add index for faster lookups by DocumentType
                entity.HasIndex(e => e.DocumentType).IsUnique().HasDatabaseName("IX_CorrespondenceDocumentType_DocumentType");
            });
            modelBuilder.Entity<CorrespondenceRequest>()
                .Property(e => e.VFResponseId)
                .HasColumnName("VFResponseId")
                .HasMaxLength(100);

            // Configure ChatConversation / ChatMessage entities (AI assistant chat history)
            modelBuilder.Entity<ChatConversationEntity>(entity =>
            {
                entity.ToTable("AiChatConversation");
                entity.HasKey(e => e.ConversationId);
                entity.Property(e => e.ConversationId).HasDefaultValueSql("NEWID()");
                entity.Property(e => e.UserId).IsRequired().HasMaxLength(200);
                entity.Property(e => e.Title).HasMaxLength(200);
                entity.Property(e => e.CreatedTS).HasDefaultValueSql("GETUTCDATE()");
                entity.Property(e => e.UpdatedTS).HasDefaultValueSql("GETUTCDATE()");
                entity.HasIndex(e => e.UserId).HasDatabaseName("IX_AiChatConversation_UserId");
                entity.HasMany(e => e.Messages)
                      .WithOne()
                      .HasForeignKey(e => e.ConversationId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<ChatMessageEntity>(entity =>
            {
                entity.ToTable("AiChatMessage");
                entity.HasKey(e => e.MessageId);
                entity.Property(e => e.MessageId).HasDefaultValueSql("NEWID()");
                entity.Property(e => e.Role).IsRequired().HasMaxLength(20);
                entity.Property(e => e.Content).IsRequired();
                entity.Property(e => e.CreatedTS).HasDefaultValueSql("GETUTCDATE()");
            });

            // Server-side AI token usage log (see AiUsageLogEntity) — centralizes what was
            // previously only tracked per-browser in localStorage.
            modelBuilder.Entity<AiUsageLogEntity>(entity =>
            {
                entity.ToTable("AiUsageLog");
                entity.HasKey(e => e.UsageId);
                entity.Property(e => e.UsageId).ValueGeneratedOnAdd();
                entity.Property(e => e.UserId).HasMaxLength(200);
                entity.Property(e => e.Source).IsRequired().HasMaxLength(20);
                entity.Property(e => e.CreatedTS).HasDefaultValueSql("GETUTCDATE()");
                entity.HasIndex(e => e.CreatedTS).HasDatabaseName("IX_AiUsageLog_CreatedTS");
            });

            //        modelBuilder.Entity<RoutingConfiguration>()
            //.Property(e => e.Route)
            //.HasConversion(
            //    v => string.Join(';', v), // or your converter
            //    v => v.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList())
            //.Metadata.SetValueComparer(
            //    new ValueComparer<List<string>>(
            //        (c1, c2) => c1.SequenceEqual(c2),
            //        c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
            //        c => c.ToList()));




        }
    }
}
