using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyTimeMultiplayer.Core.Families;

public sealed class BedReadiness
{
    private readonly HashSet<int> _ready = new HashSet<int>();

    public bool IsReady(int playerId) => _ready.Contains(playerId);

    public void MarkReady(int playerId) => _ready.Add(playerId);

    public bool Cancel(int playerId) => _ready.Remove(playerId);

    public void Reset() => _ready.Clear();

    public IReadOnlyList<int> Waiting(IEnumerable<int> playersInSession)
    {
        if (playersInSession == null)
        {
            throw new ArgumentNullException(nameof(playersInSession));
        }
        return playersInSession.Where(id => !_ready.Contains(id)).Distinct().OrderBy(id => id).ToArray();
    }

    public bool EveryoneReady(IEnumerable<int> playersInSession) => Waiting(playersInSession).Count == 0;
}
