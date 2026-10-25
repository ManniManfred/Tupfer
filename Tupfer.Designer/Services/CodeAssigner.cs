using Tupfer.Designer.Model;
using Tupfer.Designer.Oid;

namespace Tupfer.Designer.Services;

/// <summary>
/// Vergibt OID-Codes an Skripte nach demselben Verfahren wie "tttool assemble",
/// sodass bereits gedruckte Seiten gültig bleiben, wenn neue Skripte hinzukommen.
/// </summary>
public static class CodeAssigner
{
    public static void Assign(ProjectFile project)
    {
        foreach (var s in project.Scripts)
            if (int.TryParse(s.Name, out var n))
                s.Code = n;

        var used = new HashSet<int>(project.Scripts.Where(s => s.Code.HasValue).Select(s => s.Code!.Value));
        if (project.Replay.HasValue) used.Add(project.Replay.Value);
        if (project.Stop.HasValue) used.Add(project.Stop.Value);

        using var available = OidCode.CodesFor(project.ProductId).Where(c => !used.Contains(c)).GetEnumerator();
        int Next() => available.MoveNext() ? available.Current : throw new InvalidOperationException("Keine freien OID-Codes mehr.");

        // tttool reserviert immer die ersten beiden freien Codes für replay und stop
        int replay = Next();
        int stop = Next();
        project.Replay ??= replay;
        project.Stop ??= stop;

        foreach (var s in project.Scripts.Where(s => !s.Code.HasValue).OrderBy(s => s.Name, StringComparer.Ordinal))
            s.Code = Next();
    }

    /// <summary>Liefert die OID einer Fläche oder null, wenn das Ziel unbekannt ist.</summary>
    public static int? CodeFor(ProjectFile project, AreaEntry area) => area.Target switch
    {
        AreaTarget.Product => (int)project.ProductId,
        AreaTarget.Replay => project.Replay,
        AreaTarget.Stop => project.Stop,
        _ => project.Scripts.FirstOrDefault(s => s.Name == area.Script)?.Code,
    };
}
