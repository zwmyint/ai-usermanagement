using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using UserManagement.Domain.Entities;
using UserManagement.Infrastructure.Persistence;
using UserManagement.Infrastructure.Persistence.Repositories;

namespace UserManagement.Infrastructure.UnitTests.Persistence;

/// <summary>Exercises the EF Core model and UserRepository against a real (in-memory) SQLite database.</summary>
public class UserRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly UserRepository _sut;

    public UserRepositoryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
        _sut = new UserRepository(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task AddAsync_ThenGetById_RoundTripsTheUser()
    {
        var user = new User
        {
            UserName = "jdoe",
            NormalizedUserName = "JDOE",
            Email = "jdoe@example.com",
            NormalizedEmail = "JDOE@EXAMPLE.COM",
            PasswordHash = "hash",
            PasswordSalt = "salt",
            CreatedAt = DateTime.UtcNow
        };

        await _sut.AddAsync(user);
        await _db.SaveChangesAsync();

        var fetched = await _sut.GetByIdAsync(user.Id);

        Assert.NotNull(fetched);
        Assert.Equal("jdoe", fetched!.UserName);
    }

    [Fact]
    public async Task EmailExistsAsync_ReturnsTrue_OnlyForMatchingNormalizedEmail()
    {
        var user = new User
        {
            UserName = "jdoe",
            NormalizedUserName = "JDOE",
            Email = "jdoe@example.com",
            NormalizedEmail = "JDOE@EXAMPLE.COM",
            PasswordHash = "hash",
            PasswordSalt = "salt",
            CreatedAt = DateTime.UtcNow
        };
        await _sut.AddAsync(user);
        await _db.SaveChangesAsync();

        Assert.True(await _sut.EmailExistsAsync("JDOE@EXAMPLE.COM"));
        Assert.False(await _sut.EmailExistsAsync("NOBODY@EXAMPLE.COM"));
    }

    [Fact]
    public async Task SoftDeletedUser_IsExcludedFromDefaultQueries()
    {
        var user = new User
        {
            UserName = "deleted",
            NormalizedUserName = "DELETED",
            Email = "deleted@example.com",
            NormalizedEmail = "DELETED@EXAMPLE.COM",
            PasswordHash = "hash",
            PasswordSalt = "salt",
            CreatedAt = DateTime.UtcNow,
            IsDeleted = true
        };
        await _sut.AddAsync(user);
        await _db.SaveChangesAsync();

        var fetched = await _sut.GetByIdAsync(user.Id);

        Assert.Null(fetched);
    }
}
