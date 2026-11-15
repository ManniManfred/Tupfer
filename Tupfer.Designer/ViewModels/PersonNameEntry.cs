using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Tupfer.Designer.ViewModels;

/// <summary>Eine erkannte Person in der Abfrage der Namen: Ausschnitt mit dem Kopf und der eingegebene Name.</summary>
public partial class PersonNameEntry(Bitmap? face, string placeholder) : ObservableObject
{
    public Bitmap? Face { get; } = face;
    public bool HasFace => Face != null;

    /// <summary>Gilt, wenn kein Name eingegeben wird („Person 1“ …).</summary>
    public string Placeholder { get; } = placeholder;

    [ObservableProperty] string _name = "";
}
