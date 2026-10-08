namespace SmartGym.Application.Common.Exceptions;

public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
}

public class ConflictException : Exception
{
    public object? Details { get; }

    public ConflictException(string message, object? details = null) : base(message)
    {
        Details = details;
    }

    public ConflictException(string message, Exception innerException, object? details = null) : base(message, innerException)
    {
        Details = details;
    }
}

public class ForbiddenException : Exception
{
    public ForbiddenException(string message) : base(message) { }
}

public class ValidationException : Exception
{
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public ValidationException(string message) : base(message)
    {
        Errors = new Dictionary<string, string[]> { { "General", [message] } };
    }

    public ValidationException(IDictionary<string, string[]> errors)
        : base("Se produjeron uno o más errores de validación.")
    {
        Errors = new Dictionary<string, string[]>(errors);
    }
}
