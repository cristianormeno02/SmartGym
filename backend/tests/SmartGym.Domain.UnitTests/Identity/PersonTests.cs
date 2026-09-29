using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Enums;
using Xunit;

namespace SmartGym.Domain.UnitTests.Identity;

public class PersonTests
{
    [Fact]
    public void Person_CanHaveMultipleSimultaneousRoles_WithoutDuplicatingData()
    {
        // Arrange
        var person = new Person
        {
            Id = Guid.NewGuid(),
            FirstName = "Juan",
            LastName = "Pérez",
            Dni = "12345678",
            Email = "juan.perez@example.com",
            PhoneNumber = "+5491112345678"
        };

        var instructorRole = new Role
        {
            Id = (int)RoleType.Instructor,
            Name = RoleType.Instructor.ToString(),
            Description = "Instructor de clases"
        };

        var studentRole = new Role
        {
            Id = (int)RoleType.Student,
            Name = RoleType.Student.ToString(),
            Description = "Alumno del gimnasio"
        };

        // Act
        person.AddRole(instructorRole);
        person.AddRole(studentRole);

        // Assert
        Assert.Equal(2, person.PersonRoles.Count);
        Assert.True(person.HasRole(RoleType.Instructor));
        Assert.True(person.HasRole(RoleType.Student));
        Assert.False(person.HasRole(RoleType.Secretary));
        Assert.All(person.PersonRoles, pr => Assert.Equal(person.Id, pr.PersonId));
    }

    [Fact]
    public void Person_CanBeLinkedToUser_WithCredentialsAndGoogleIdentity()
    {
        // Arrange
        var person = new Person
        {
            Id = Guid.NewGuid(),
            FirstName = "María",
            LastName = "Gómez",
            Dni = "87654321",
            Email = "maria.gomez@example.com"
        };

        var user = new User
        {
            Id = Guid.NewGuid(),
            PersonId = person.Id,
            Username = "maria.gomez@example.com",
            PasswordHash = "hashed_password",
            GoogleSubjectId = "google_123456789"
        };

        // Act
        person.SetUser(user);

        // Assert
        Assert.NotNull(person.User);
        Assert.Equal(person.Id, person.User.PersonId);
        Assert.Equal("google_123456789", person.User.GoogleSubjectId);
    }
}
