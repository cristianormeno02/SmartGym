using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartGym.Domain.Entities.Activities;

namespace SmartGym.Infrastructure.Persistence.Configurations.Activities;

public class RoomConfiguration : IEntityTypeConfiguration<Room>
{
    public void Configure(EntityTypeBuilder<Room> builder)
    {
        builder.ToTable("Rooms");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(r => r.Description)
            .HasMaxLength(500);

        builder.Property(r => r.Capacity)
            .IsRequired();

        // Seed default rooms: Indoor y Outdoor
        builder.HasData(
            new Room
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Name = "Sala Indoor",
                Description = "Sala principal climatizada con piso amortiguado y equipamiento para clases grupales.",
                Capacity = 30,
                CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                IsActive = true
            },
            new Room
            {
                Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Name = "Sala Outdoor",
                Description = "Espacio exterior techado para entrenamientos de alta intensidad, running y funcional.",
                Capacity = 40,
                CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                IsActive = true
            }
        );
    }
}
