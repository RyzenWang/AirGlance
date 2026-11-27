namespace AirGlance.Core.Files;

/// <summary>Which file fact a folder listing is ordered by.</summary>
public enum FolderSortField
{
    Name,
    DateModified,
    Size,
    Type
}

/// <summary>
/// One folder ordering: a field and a direction, persisted as a single settings string
/// (<c>name</c>, <c>date-descending</c>, <c>size</c>, <c>type-descending</c>, ...). Parsing
/// is tolerant, so a hand-edited or older settings file can never stop a folder opening:
/// anything unrecognised falls back to the natural name order.
/// </summary>
public readonly record struct FolderSortOrder(FolderSortField Field, bool Descending)
{
    public static FolderSortOrder Default => new(FolderSortField.Name, false);

    public static FolderSortOrder Ascending(FolderSortField field) => new(field, false);

    public static FolderSortOrder Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Default;
        }

        var text = value.Trim();
        var descending = false;
        if (text.EndsWith("-descending", StringComparison.OrdinalIgnoreCase))
        {
            descending = true;
            text = text[..^"-descending".Length];
        }

        var field = text.Trim().ToLowerInvariant() switch
        {
            "date" or "date-modified" or "modified" or "datecreated" => FolderSortField.DateModified,
            "size" => FolderSortField.Size,
            "type" or "extension" => FolderSortField.Type,
            _ => FolderSortField.Name
        };

        return new FolderSortOrder(field, descending);
    }

    /// <summary>The value stored in <c>settings.json</c> and shown in the settings window.</summary>
    public string ToSettingValue()
    {
        var name = Field switch
        {
            FolderSortField.DateModified => "date",
            FolderSortField.Size => "size",
            FolderSortField.Type => "type",
            _ => "name"
        };

        return Descending ? name + "-descending" : name;
    }

    public override string ToString() => ToSettingValue();

    /// <summary>The field plus its opposite direction, used by the "reverse" command.</summary>
    public FolderSortOrder Reversed() => this with { Descending = !Descending };
}
