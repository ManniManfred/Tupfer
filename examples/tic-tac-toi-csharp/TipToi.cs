// Platzhalter für die IDE: Mit dieser Datei lassen sich Stift-Programme in Visual Studio, Rider
// oder VS Code mit IntelliSense schreiben. Beim Übersetzen im Tupfer Designer werden diese Klassen
// ignoriert – sie können also auch mit in das Programm kopiert werden.
using System;

/// <summary>Macht eine Methode zu einem Skript, das beim Antippen eines Codes läuft.</summary>
[AttributeUsage(AttributeTargets.Method)]
public class OidAttribute : Attribute
{
    public OidAttribute() { }

    /// <param name="skriptName">Eigener Skriptname statt des Methodennamens.</param>
    public OidAttribute(string skriptName) { }
}

/// <summary>Basisklasse für Stift-Programme. Felder sind Register (ganze Zahlen 0 bis 65535 oder bool).</summary>
public abstract class TipToi
{
    /// <summary>Spielt einen Text aus der Sprachausgabe oder eine Audiodatei; mehrere Namen nacheinander.</summary>
    protected void Play(params string[] namen) { }

    /// <summary>Spielt alle Namen nacheinander.</summary>
    protected void PlayAll(params string[] namen) { }

    /// <summary>Spielt zufällig einen der Namen.</summary>
    protected void PlayRandom(params string[] namen) { }

    /// <summary>Pseudo-Zufallszahl von 0 bis obergrenze - 1 (obergrenze muss konstant sein).</summary>
    protected int Random(int obergrenze) => 0;

    /// <summary>Zählerstand des Stifts modulo n (steigt mit der Zeit an).</summary>
    protected int Timer(int modulo) => 0;

    /// <summary>Bricht die Wiedergabe ab.</summary>
    protected void Cancel() { }
}
