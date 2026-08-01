using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NeuronCodec.Web.Data;
using NeuronCodec.Web.Data.Entities;

namespace NeuronCodec.Web.Services;

public record MediaResult(bool Ok, string? Error, MediaFile? File)
{
    public static MediaResult Fail(string error) => new(false, error, null);
    public static MediaResult Success(MediaFile file) => new(true, null, file);
}

/// <summary>
/// Owns the uploads volume. Every path is resolved against the configured root and re-checked,
/// so a crafted filename cannot escape the directory.
/// </summary>
public class MediaStorage(AppDbContext db, IOptions<NeuronCodecOptions> options, ILogger<MediaStorage> logger)
{
    private readonly UploadOptions _options = options.Value.Uploads;

    public string RootPath => Path.GetFullPath(_options.Path);

    public void EnsureRoot() => Directory.CreateDirectory(RootPath);

    public async Task<MediaResult> SaveAsync(IFormFile upload, string? altText, CancellationToken ct = default)
    {
        if (upload.Length == 0)
            return MediaResult.Fail("The file is empty.");

        if (upload.Length > _options.MaxBytes)
            return MediaResult.Fail($"The file is larger than the {_options.MaxBytes / (1024 * 1024)} MB limit.");

        var extension = Path.GetExtension(upload.FileName).ToLowerInvariant();
        if (!_options.AllowedExtensions.Contains(extension))
            return MediaResult.Fail($"Files of type '{extension}' are not allowed.");

        var baseName = Slug.From(Path.GetFileNameWithoutExtension(upload.FileName), "file");
        var storedName = await UniqueStoredNameAsync(baseName, extension, ct);

        EnsureRoot();
        var destination = ResolveInsideRoot(storedName);

        await using (var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await upload.CopyToAsync(stream, ct);
        }

        var media = new MediaFile
        {
            StoredName = storedName,
            OriginalName = Path.GetFileName(upload.FileName),
            ContentType = string.IsNullOrWhiteSpace(upload.ContentType)
                ? "application/octet-stream"
                : upload.ContentType,
            SizeBytes = upload.Length,
            AltText = string.IsNullOrWhiteSpace(altText) ? null : altText.Trim(),
        };

        var dimensions = TryReadImageSize(destination, extension);
        if (dimensions is not null)
        {
            media.Width = dimensions.Value.Width;
            media.Height = dimensions.Value.Height;
        }

        db.MediaFiles.Add(media);
        await db.SaveChangesAsync(ct);
        return MediaResult.Success(media);
    }

    /// <summary>
    /// Renames the file on disk and in the database. The extension is preserved — changing it
    /// would break the served content type.
    /// </summary>
    public async Task<MediaResult> RenameAsync(int id, string newName, CancellationToken ct = default)
    {
        var media = await db.MediaFiles.FindAsync([id], ct);
        if (media is null) return MediaResult.Fail("File not found.");

        var extension = Path.GetExtension(media.StoredName);
        var baseName = Slug.From(Path.GetFileNameWithoutExtension(newName), "file");
        var storedName = await UniqueStoredNameAsync(baseName, extension, ct, exceptId: id);

        if (storedName == media.StoredName)
        {
            media.OriginalName = Path.GetFileName(newName);
            await db.SaveChangesAsync(ct);
            return MediaResult.Success(media);
        }

        var from = ResolveInsideRoot(media.StoredName);
        var to = ResolveInsideRoot(storedName);

        if (File.Exists(from))
        {
            File.Move(from, to);
        }
        else
        {
            logger.LogWarning("Media {Id} has no file at {Path}; renaming the record only.", id, from);
        }

        media.StoredName = storedName;
        media.OriginalName = Path.GetFileName(newName);
        await db.SaveChangesAsync(ct);
        return MediaResult.Success(media);
    }

    /// <summary>
    /// Deletes a file. Refuses while an article still points at it as a featured or OG image,
    /// so a delete can never silently break a published page.
    /// </summary>
    public async Task<MediaResult> DeleteAsync(int id, CancellationToken ct = default)
    {
        var media = await db.MediaFiles.FindAsync([id], ct);
        if (media is null) return MediaResult.Fail("File not found.");

        var usedBy = await db.Articles
            .Where(a => a.FeaturedMediaId == id || a.OgImageId == id)
            .Select(a => a.Title)
            .ToListAsync(ct);

        if (usedBy.Count > 0)
            return MediaResult.Fail($"Still used by: {string.Join(", ", usedBy)}.");

        var path = ResolveInsideRoot(media.StoredName);
        if (File.Exists(path))
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException ex)
            {
                logger.LogError(ex, "Could not delete media file {Path}.", path);
                return MediaResult.Fail("The file is locked and could not be deleted.");
            }
        }

        db.MediaFiles.Remove(media);
        await db.SaveChangesAsync(ct);
        return MediaResult.Success(media);
    }

    public async Task UpdateAltTextAsync(int id, string? altText, CancellationToken ct = default)
    {
        var media = await db.MediaFiles.FindAsync([id], ct);
        if (media is null) return;

        media.AltText = string.IsNullOrWhiteSpace(altText) ? null : altText.Trim();
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Picks a filename that is free both in the database and on disk, appending -2, -3, …
    /// before the extension on collision.
    /// </summary>
    private async Task<string> UniqueStoredNameAsync(string baseName, string extension, CancellationToken ct, int? exceptId = null)
    {
        var taken = (await db.MediaFiles
                .Where(m => exceptId == null || m.Id != exceptId)
                .Select(m => m.StoredName)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        bool IsFree(string name) =>
            !taken.Contains(name) && !File.Exists(Path.Combine(RootPath, name));

        var candidate = baseName + extension;
        if (IsFree(candidate)) return candidate;

        for (var n = 2; n < 10_000; n++)
        {
            candidate = $"{baseName}-{n}{extension}";
            if (IsFree(candidate)) return candidate;
        }

        return $"{baseName}-{Guid.NewGuid():N}{extension}";
    }

    /// <summary>
    /// Resolves <paramref name="storedName"/> inside the uploads root, rejecting anything that
    /// resolves outside it.
    /// </summary>
    private string ResolveInsideRoot(string storedName)
    {
        var root = RootPath;
        var full = Path.GetFullPath(Path.Combine(root, Path.GetFileName(storedName)));

        if (!full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Resolved path '{full}' is outside the uploads root.");
        }

        return full;
    }

    /// <summary>
    /// Reads image dimensions from the file header for the formats with a cheap, well-known
    /// layout. Returns null for anything else — dimensions are a nicety, not a requirement.
    /// </summary>
    private static (int Width, int Height)? TryReadImageSize(string path, string extension)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);

            return extension switch
            {
                ".png" => ReadPng(reader),
                ".gif" => ReadGif(reader),
                ".jpg" or ".jpeg" => ReadJpeg(reader),
                _ => null,
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static (int, int)? ReadPng(BinaryReader r)
    {
        // 8-byte signature, 4-byte length, "IHDR", then width and height as big-endian uint32.
        if (r.BaseStream.Length < 24) return null;
        r.BaseStream.Position = 16;
        var width = ReadBigEndianInt32(r);
        var height = ReadBigEndianInt32(r);
        return (width, height);
    }

    private static (int, int)? ReadGif(BinaryReader r)
    {
        if (r.BaseStream.Length < 10) return null;
        r.BaseStream.Position = 6;
        return (r.ReadUInt16(), r.ReadUInt16()); // little-endian
    }

    private static (int, int)? ReadJpeg(BinaryReader r)
    {
        // Walk the marker segments to the start-of-frame, which carries the dimensions.
        if (r.ReadUInt16() != 0xD8FF) return null; // SOI, byte-swapped by the little-endian read

        while (r.BaseStream.Position < r.BaseStream.Length - 1)
        {
            if (r.ReadByte() != 0xFF) continue;

            byte marker;
            do { marker = r.ReadByte(); } while (marker == 0xFF);

            // SOF0-SOF3, SOF5-SOF7, SOF9-SOF11, SOF13-SOF15 all carry the frame header.
            if (marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC))
            {
                r.BaseStream.Position += 3; // segment length (2) + sample precision (1)
                var height = ReadBigEndianUInt16(r);
                var width = ReadBigEndianUInt16(r);
                return (width, height);
            }

            var length = ReadBigEndianUInt16(r);
            if (length < 2) return null;
            r.BaseStream.Position += length - 2;
        }

        return null;
    }

    private static int ReadBigEndianInt32(BinaryReader r)
    {
        var bytes = r.ReadBytes(4);
        return (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];
    }

    private static ushort ReadBigEndianUInt16(BinaryReader r)
    {
        var bytes = r.ReadBytes(2);
        return (ushort)((bytes[0] << 8) | bytes[1]);
    }
}
