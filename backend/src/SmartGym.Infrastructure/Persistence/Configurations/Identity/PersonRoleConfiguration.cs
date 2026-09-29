using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartGym.Domain.Entities.Identity;

namespace SmartGym.Infrastructure.Persistence.Configurations.Identity;

public class PersonRoleConfiguration : IEntityTypeConfiguration<PersonRole>
{
    public void Configure(EntityTypeBuilder<PersonRole> builder)
    {
        builder.ToTable("PersonRoles");

        builder.HasKey(pr => new { pr.PersonId, pr.RoleId });

        builder.HasOne(pr => pr.Person)
            .WithMany(p => p.PersonRoles)
            .HasForeignKey(pr => pr.PersonId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(pr => pr.Role)
            .WithMany(r => r.PersonRoles)
            .HasForeignKey(pr => pr.RoleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
