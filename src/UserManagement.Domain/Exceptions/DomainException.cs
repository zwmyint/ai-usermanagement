namespace UserManagement.Domain.Exceptions;

public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
}

public class NotFoundException : DomainException
{
    public NotFoundException(string message) : base(message) { }

    public static NotFoundException For(string entity, object key) =>
        new($"{entity} with identifier '{key}' was not found.");
}

public class ConflictException : DomainException
{
    public ConflictException(string message) : base(message) { }
}

public class ValidationException : DomainException
{
    public IDictionary<string, string[]> Errors { get; }

    public ValidationException(IDictionary<string, string[]> errors)
        : base("One or more validation errors occurred.") => Errors = errors;

    public ValidationException(string field, string error)
        : this(new Dictionary<string, string[]> { [field] = new[] { error } }) { }
}

public class ForbiddenException : DomainException
{
    public ForbiddenException(string message) : base(message) { }
}
