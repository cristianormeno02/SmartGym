using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartGym.Domain.Entities.Identity;

namespace SmartGym.Infrastructure.Persistence.Configurations.Identity;

public class PersonConfiguration : IEntityTypeConfiguration<Person>
{
    public void Configure(EntityTypeBuilder<Person> builder)
    {
        builder.ToTable("People", t =>
        {
            t.HasCheckConstraint(
                "CK_People_Document_Consistency",
                "(\"DocumentType\" IS NULL AND \"DocumentIssuingCountry\" IS NULL AND \"DocumentNumber\" IS NULL AND \"DocumentNumberNormalized\" IS NULL) OR " +
                "(\"DocumentType\" IS NOT NULL AND \"DocumentIssuingCountry\" IS NOT NULL AND \"DocumentNumber\" IS NOT NULL AND \"DocumentNumberNormalized\" IS NOT NULL)");
        });

        builder.HasKey(p => p.Id);

        // Concurrencia optimista con PostgreSQL xmin
        builder.Property(p => p.Version).IsRowVersion();

        builder.Property(p => p.FirstName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(p => p.LastName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(p => p.SearchName)
            .IsRequired()
            .HasMaxLength(201);

        builder.Property(p => p.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(p => p.StatusReason)
            .HasMaxLength(500);

        builder.Property(p => p.Gender)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(p => p.Email)
            .IsRequired(false)
            .HasMaxLength(256);

        builder.HasIndex(p => p.Email)
            .IsUnique()
            .HasDatabaseName("IX_People_Email_Unique")
            .HasFilter("\"Email\" IS NOT NULL");

        builder.Property(p => p.PrimaryPhone)
            .HasMaxLength(30);

        builder.Property(p => p.SecondaryPhone)
            .HasMaxLength(30);

        builder.Property(p => p.ExternalAvatarUrl)
            .HasMaxLength(500);

        builder.Property(p => p.MedicalCertificateUrl)
            .HasMaxLength(500);

        // Owned Value Object: IdentificationDocument
        builder.OwnsOne(p => p.Document, doc =>
        {
            doc.Property(d => d.Type)
                .HasColumnName("DocumentType")
                .HasConversion<string>()
                .HasMaxLength(20);

            doc.Property(d => d.IssuingCountry)
                .HasColumnName("DocumentIssuingCountry")
                .HasMaxLength(2);

            doc.Property(d => d.Number)
                .HasColumnName("DocumentNumber")
                .HasMaxLength(50);

            doc.Property(d => d.NormalizedNumber)
                .HasColumnName("DocumentNumberNormalized")
                .HasMaxLength(50);

            doc.HasIndex(d => new { d.Type, d.IssuingCountry, d.NormalizedNumber })
                .IsUnique()
                .HasDatabaseName("IX_People_Document_Unique")
                .HasFilter("\"DocumentNumberNormalized\" IS NOT NULL");
        });

        // Owned Value Object: Address
        builder.OwnsOne(p => p.Address, a =>
        {
            a.Property(x => x.Street).HasColumnName("AddressStreet").HasMaxLength(150);
            a.Property(x => x.Number).HasColumnName("AddressNumber").HasMaxLength(20);
            a.Property(x => x.Floor).HasColumnName("AddressFloor").HasMaxLength(10);
            a.Property(x => x.Apartment).HasColumnName("AddressApartment").HasMaxLength(10);
            a.Property(x => x.PostalCode).HasColumnName("AddressPostalCode").HasMaxLength(20);
            a.Property(x => x.City).HasColumnName("AddressCity").HasMaxLength(100);
            a.Property(x => x.StateProvince).HasColumnName("AddressStateProvince").HasMaxLength(100);
            a.Property(x => x.CountryCode).HasColumnName("AddressCountryCode").HasMaxLength(2);
        });

        // Owned Value Object: ProfileImage
        builder.OwnsOne(p => p.ProfileImage, img =>
        {
            img.Property(x => x.Key).HasColumnName("ProfileImageKey").HasMaxLength(500);
            img.Property(x => x.ContentType).HasColumnName("ProfileImageContentType").HasMaxLength(50);
            img.Property(x => x.SizeBytes).HasColumnName("ProfileImageSizeBytes");
            img.Property(x => x.UploadedAtUtc).HasColumnName("ProfileImageUploadedAtUtc");
        });

        // Owned Value Object: EmergencyContact
        builder.OwnsOne(p => p.EmergencyContact, ec =>
        {
            ec.Property(x => x.Name).HasColumnName("EmergencyContactName").HasMaxLength(150);
            ec.Property(x => x.Phone).HasColumnName("EmergencyContactPhone").HasMaxLength(50);
            ec.Property(x => x.Relationship).HasColumnName("EmergencyContactRelationship").HasMaxLength(50);
            ec.Ignore(x => x.IsComplete);
        });

        // Indexes
        builder.HasIndex(p => p.SearchName)
            .HasMethod("gin")
            .HasOperators("gin_trgm_ops")
            .HasDatabaseName("IX_People_SearchName_Trgm");

        builder.HasIndex(p => new { p.LastName, p.FirstName })
            .HasDatabaseName("IX_People_LastName_FirstName");

        // Ignored unmapped helper properties
        builder.Ignore(p => p.FullName);
        builder.Ignore(p => p.IsMedicalCertificateValid);
        builder.Ignore(p => p.PhoneNumber);
        builder.Ignore(p => p.PhotoUrl);
        builder.Ignore(p => p.Dni);
        builder.Ignore(p => p.EmergencyContactName);
        builder.Ignore(p => p.EmergencyContactPhone);
        builder.Ignore(p => p.EmergencyContactRelationship);

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
