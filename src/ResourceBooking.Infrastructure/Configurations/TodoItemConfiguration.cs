using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Configuration;
using ResourceBooking.Core.Entities;

namespace ResourceBooking.Infrastructure.Configurations;

public class TodoItemConfiguration : IEntityTypeConfiguration<TodoItem>
{
    public void Configure(EntityTypeBuilder<TodoItem> builder)
    {
        //These chained configuration methods are called EF Core’s Fluent API.
        builder.ToTable("TodoItems");

        builder.HasKey(t => t.Id);
        
        builder.Property(t => t.Title)
            .IsRequired()
            .HasMaxLength(250);
        
        builder.HasOne(t => t.User) // this says that Each todo references one user
            .WithMany()             // a user can have many todos
            .HasForeignKey(t => t.UserId)   // UserId stores the referenced user’s ID.
            .OnDelete(DeleteBehavior.Restrict);//A user cannot be physically deleted while todo rows still reference them.

        // creates composite index containing (UserId, IsDone) so that queries are more efficient
        builder.HasIndex(t => new { t.UserId, t.IsDone });
        
        // this will run by default for every query
        builder.HasQueryFilter(t => !t.IsDeleted);
    }
}
