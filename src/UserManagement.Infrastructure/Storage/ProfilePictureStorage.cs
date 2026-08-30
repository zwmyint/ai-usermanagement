using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using UserManagement.Application.Common.Settings;
using UserManagement.Application.Interfaces;
using UserManagement.Domain.Exceptions;

namespace UserManagement.Infrastructure.Storage;

public sealed class ProfilePictureStorage : IProfilePictureStorage
{
    private const string PublicPathPrefix = "/uploads/profile-pictures/";
    private readonly string _storagePath;
    private readonly ProfilePictureSettings _settings;

    public ProfilePictureStorage(IOptions<ProfilePictureSettings> settings)
    {
        _settings = settings.Value;
        _storagePath = Path.GetFullPath(_settings.StoragePath);
    }

    public async Task<string> SaveAsync(Stream image, string fileName, CancellationToken ct = default)
    {
        if (image.CanSeek && image.Length > _settings.MaxFileSizeBytes)
            throw new ValidationException("ProfilePicture", $"Profile pictures must be {_settings.MaxFileSizeBytes / 1024 / 1024} MB or smaller.");

        try
        {
            using var decoded = await Image.LoadAsync(image, ct);
            var format = decoded.Metadata.DecodedImageFormat?.Name;
            if (!string.Equals(format, "JPEG", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(format, "PNG", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(format, "WEBP", StringComparison.OrdinalIgnoreCase))
            {
                throw new ValidationException("ProfilePicture", "Profile pictures must be a JPEG, PNG, or WebP image.");
            }

            decoded.Mutate(context => context.Resize(new ResizeOptions
            {
                Size = new Size(_settings.MaxWidth, _settings.MaxHeight),
                Mode = ResizeMode.Max
            }));

            Directory.CreateDirectory(_storagePath);
            var storedFileName = $"{Guid.NewGuid():N}.jpg";
            var finalPath = Path.Combine(_storagePath, storedFileName);
            var temporaryPath = $"{finalPath}.tmp";
            try
            {
                await decoded.SaveAsync(temporaryPath, new JpegEncoder { Quality = 85 }, ct);
                File.Move(temporaryPath, finalPath);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }

            return $"{PublicPathPrefix}{storedFileName}";
        }
        catch (UnknownImageFormatException)
        {
            throw new ValidationException("ProfilePicture", "The uploaded file is not a supported image.");
        }
    }

    public Task DeleteAsync(string? profilePicturePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(profilePicturePath) ||
            !profilePicturePath.StartsWith(PublicPathPrefix, StringComparison.Ordinal))
            return Task.CompletedTask;

        var fileName = Path.GetFileName(profilePicturePath);
        if (!string.Equals(profilePicturePath, $"{PublicPathPrefix}{fileName}", StringComparison.Ordinal))
            return Task.CompletedTask;

        var path = Path.Combine(_storagePath, fileName);
        if (File.Exists(path))
            File.Delete(path);

        return Task.CompletedTask;
    }
}
