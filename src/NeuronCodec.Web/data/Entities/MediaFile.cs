namespace NeuronCodec.Web.Data.Entities;

public class MediaFile
{
    public int Id { get; set; }

    /// <summary>Name on disk inside the uploads volume. Unique, slug-safe.</summary>
    public string StoredName { get; set; } = "";

    /// <summary>The filename as uploaded, kept for display.</summary>
    public string OriginalName { get; set; } = "";

    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }

    public int? Width { get; set; }
    public int? Height { get; set; }

    /// <summary>Default alt text, used when the file is inserted from the media picker.</summary>
    public string? AltText { get; set; }

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    public bool IsImage => ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    /// <summary>Public URL path. Served by the static-file mapping for the uploads volume.</summary>
    public string Url => "/uploads/" + StoredName;

    /// <summary>Extension including the dot, taken from the name on disk.</summary>
    public string Extension => Path.GetExtension(StoredName);

    /// <summary>Display name without its extension — what the rename field edits.</summary>
    public string DisplayNameWithoutExtension => Path.GetFileNameWithoutExtension(OriginalName);
}
