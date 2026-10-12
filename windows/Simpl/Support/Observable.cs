using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Simpl.Support;

/// <summary>A class whose properties the screens follow (SwiftUI's ObservableObject, WPF's INotifyPropertyChanged).</summary>
public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Changed(name);
        return true;
    }

    protected void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
