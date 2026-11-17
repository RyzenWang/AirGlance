namespace AirGlance.Core.Navigation;

public readonly record struct NavigationStep(string Path, int Index, bool Wrapped);

/// <summary>
/// The one-folder browse model. Moving past either end wraps to the other end and says
/// so, instead of disabling the command or opening a dialog.
/// </summary>
public sealed class ImageNavigator
{
    private readonly List<string> _files;
    private int _index;

    public ImageNavigator(IEnumerable<string> files, int index = 0)
    {
        _files = [.. files];
        _index = _files.Count == 0 ? -1 : Math.Clamp(index, 0, _files.Count - 1);
    }

    public IReadOnlyList<string> Files => _files;

    public int Count => _files.Count;

    public int CurrentIndex => _index;

    public string? CurrentPath => _index >= 0 && _index < _files.Count ? _files[_index] : null;

    public bool HasImages => _files.Count > 0;

    public NavigationStep? MoveNext()
    {
        if (_files.Count == 0)
        {
            return null;
        }

        var wrapped = _files.Count > 1 && _index == _files.Count - 1;
        _index = (_index + 1) % _files.Count;
        return new NavigationStep(_files[_index], _index, wrapped);
    }

    public NavigationStep? MovePrevious()
    {
        if (_files.Count == 0)
        {
            return null;
        }

        var wrapped = _files.Count > 1 && _index == 0;
        _index = (_index - 1 + _files.Count) % _files.Count;
        return new NavigationStep(_files[_index], _index, wrapped);
    }

    public NavigationStep? GoTo(int index)
    {
        if (_files.Count == 0)
        {
            return null;
        }

        var target = Math.Clamp(index, 0, _files.Count - 1);
        if (target == _index)
        {
            return null;
        }

        _index = target;
        return new NavigationStep(_files[_index], _index, false);
    }

    public void Reset(IEnumerable<string> files, int index = 0)
    {
        _files.Clear();
        _files.AddRange(files);
        _index = _files.Count == 0 ? -1 : Math.Clamp(index, 0, _files.Count - 1);
    }
}
