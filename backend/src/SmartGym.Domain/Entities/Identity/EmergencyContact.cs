namespace SmartGym.Domain.Entities.Identity;

public class EmergencyContact
{
    public string? Name { get; private set; }
    public string? Phone { get; private set; }
    public string? Relationship { get; private set; }

    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(Name) &&
        !string.IsNullOrWhiteSpace(Phone) &&
        !string.IsNullOrWhiteSpace(Relationship);

    private EmergencyContact() { }

    private EmergencyContact(string? name, string? phone, string? relationship)
    {
        Name = name?.Trim();
        Phone = phone?.Trim();
        Relationship = relationship?.Trim();
    }

    public static EmergencyContact Create(string? name, string? phone, string? relationship)
    {
        return new EmergencyContact(name, phone, relationship);
    }
}
