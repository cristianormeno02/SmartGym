using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartGym.Domain.Entities.Activities;

namespace SmartGym.Infrastructure.Persistence.Configurations.Activities;

public class ActivityConfiguration : IEntityTypeConfiguration<Activity>
{
    public void Configure(EntityTypeBuilder<Activity> builder)
    {
        builder.ToTable("Activities");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(a => a.Summary)
            .HasMaxLength(250);

        builder.Property(a => a.Description)
            .HasMaxLength(2000);

        builder.Property(a => a.MinCapacity)
            .IsRequired();

        builder.Property(a => a.MaxCapacity)
            .IsRequired();

        builder.Property(a => a.LogoUrl)
            .HasMaxLength(500);

        var stringListComparer = new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<string>>(
            (c1, c2) => c1 != null && c2 != null ? c1.SequenceEqual(c2) : c1 == c2,
            c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
            c => c.ToList()
        );

        builder.Property(a => a.ImageUrls)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => string.IsNullOrEmpty(v) ? new List<string>() : JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>()
            )
            .Metadata.SetValueComparer(stringListComparer);

        builder.Property(a => a.Status)
            .IsRequired();

        builder.HasOne(a => a.DefaultRoom)
            .WithMany(r => r.Activities)
            .HasForeignKey(a => a.DefaultRoomId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
