using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Enums;

namespace SmartGym.Infrastructure.Persistence.Configurations.Identity;

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("Roles");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.Name)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(r => r.Description)
            .HasMaxLength(250);

        // Seed inicial de roles del sistema
        builder.HasData(
            new Role { Id = (int)RoleType.Administrator, Name = "Administrator", Description = "Administrador con control total del sistema y gimnasio" },
            new Role { Id = (int)RoleType.Secretary, Name = "Secretary", Description = "Secretario de recepción, pagos, turnos y control de asistencias" },
            new Role { Id = (int)RoleType.Instructor, Name = "Instructor", Description = "Instructor encargado del dictado de clases y visualización de alumnos" },
            new Role { Id = (int)RoleType.Student, Name = "Student", Description = "Alumno habilitado para reservas, asistencias y contratación de planes" },
            new Role { Id = (int)RoleType.Public, Name = "Public", Description = "Usuario público o prospecto con acceso a información general" }
        );
    }
}
