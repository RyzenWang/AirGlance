namespace AirGlance.App.ViewModels;

/// <summary>One label/value line in the information panel.</summary>
public sealed class InfoRow
{
    public InfoRow(string label, string value, bool isMono = false)
    {
        Label = label;
        Value = value;
        IsMono = isMono;
    }

    public string Label { get; }

    public string Value { get; }

    public bool IsMono { get; }
}

/// <summary>A titled group of rows. Empty groups are not added, so no "—" placeholders appear.</summary>
public sealed class InfoSection
{
    public InfoSection(string title, IReadOnlyList<InfoRow> rows)
    {
        Title = title;
        Rows = rows;
    }

    public string Title { get; }

    public IReadOnlyList<InfoRow> Rows { get; }
}
