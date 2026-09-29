namespace SmartGym.Application.Common.Security;

public static class Roles
{
    public const string Administrator = "Administrator";
    public const string Secretary = "Secretary";
    public const string Instructor = "Instructor";
    public const string Student = "Student";
    public const string Public = "Public";

    public const string Staff = $"{Administrator},{Secretary}";
    public const string TeachingAndStaff = $"{Administrator},{Secretary},{Instructor}";
    public const string AllAuthenticated = $"{Administrator},{Secretary},{Instructor},{Student}";
}

public static class Policies
{
    public const string RequireAdministrator = "RequireAdministrator";
    public const string RequireSecretary = "RequireSecretary";
    public const string RequireInstructor = "RequireInstructor";
    public const string RequireStudent = "RequireStudent";
    public const string RequireStaff = "RequireStaff";
    public const string RequireTeachingAndStaff = "RequireTeachingAndStaff";
}
