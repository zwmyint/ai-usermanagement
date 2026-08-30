namespace UserManagement.Domain.Interfaces;

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default);
}

public interface IPasswordHasher
{
    /// <summary>Hashes a plaintext password, producing a hash and the salt used.</summary>
    (string Hash, string Salt) Hash(string password);

    /// <summary>Verifies a plaintext password against a stored hash/salt pair in constant time.</summary>
    bool Verify(string password, string hash, string salt);
}
