using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartGym.Domain.Entities.Identity;

namespace SmartGym.Infrastructure.Persistence.Configurations.Identity;

public class PersonConfiguration : IEntityTypeConfiguration<Person>
{
    public void Configure(EntityTypeBuilder<Person> builder)
    {
        builder.ToTable("People");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.FirstName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(p => p.LastName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(p => p.Dni)
            .IsRequired()
            .HasMaxLength(20);

        builder.HasIndex(p => p.Dni)
            .IsUnique();

        builder.Property(p => p.Email)
            .IsRequired()
            .HasMaxLength(256);

        builder.HasIndex(p => p.Email)
            .IsUnique();

        builder.Property(p => p.PhoneNumber)
            .HasMaxLength(50);

        builder.Property(p => p.PhotoUrl)
            .HasMaxLength(500);

        builder.Property(p => p.MedicalCertificateUrl)
            .HasMaxLength(500);

        builder.Property(p => p.EmergencyContactName)
            .HasMaxLength(150);

        builder.Property(p => p.EmergencyContactPhone)
            .HasMaxLength(50);

        builder.Property(p => p.EmergencyContactRelationship)
            .HasMaxLength(50);

        builder.Ignore(p => p.FullName);
        builder.Ignore(p => p.IsMedicalCertificateValid);

        // Relación 1-a-1 con Usuario
        builder.HasOne(p => p.User)
            .WithOne(u => u.Person)
            .HasForeignKey<User>(u => u.PersonId)
            .OnDelete(DeleteBehavior.Cascade);

        // Relación 1-a-N con PersonRole
        builder.HasMany(p => p.PersonRoles)
            .WithOne(pr => pr.Person)
            .HasForeignKey(pr => pr.PersonId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
