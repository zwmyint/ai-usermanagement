namespace UserManagement.Application.Interfaces;

public interface IProfilePictureStorage
{
    Task<string> SaveAsync(Stream image, string fileName, CancellationToken ct = default);
    Task DeleteAsync(string? profilePicturePath, CancellationToken ct = default);
}
