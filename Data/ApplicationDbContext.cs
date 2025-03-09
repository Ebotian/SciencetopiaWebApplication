using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Sciencetopia.Models;

namespace Sciencetopia.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // DbSets for messaging and user activity
        public DbSet<Message> Messages { get; set; }
        public DbSet<Conversation> Conversations { get; set; }
        public DbSet<Notification> Notifications { get; set; }
        public DbSet<VisitLog> VisitLogs { get; set; } // New VisitLog DbSet
        // Add the DailySummaries DbSet
        public DbSet<DailySummary> DailySummaries { get; set; }
        // Add the KnowledgeNodes DbSet
        public DbSet<KnowledgeNode> KnowledgeNodes { get; set; }
        public DbSet<TypesOfTags> TypesOfTags { get; set; }
        public DbSet<TagTypes> TagTypes { get; set; }
        // Add the Tags DbSet
        public DbSet<Tags> Tags { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // Configure relationships for the Message model
            builder.Entity<Message>()
                .HasOne(m => m.Sender)
                .WithMany()
                .HasForeignKey(m => m.SenderId)
                .OnDelete(DeleteBehavior.Restrict); // Prevent cascade delete

            builder.Entity<Message>()
                .HasOne(m => m.Receiver)
                .WithMany()
                .HasForeignKey(m => m.ReceiverId)
                .OnDelete(DeleteBehavior.Restrict);

            // Configure VisitLog relationships (if needed)
            builder.Entity<VisitLog>()
                .HasKey(v => v.Id); // Primary Key

            builder.Entity<VisitLog>()
                .HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(v => v.UserId)
                .OnDelete(DeleteBehavior.Restrict); // If visits are linked to users

            // Define Composite Primary Key for TagTypes (No Navigation Properties)
            builder.Entity<TagTypes>()
                .HasKey(tt => new { tt.TagId, tt.TypeId });  // Define composite key
        }
    }
}
