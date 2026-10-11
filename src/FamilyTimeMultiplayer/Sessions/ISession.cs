using System;
using System.Collections;
using System.Collections.Generic;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Game;
using UnityEngine;

namespace FamilyTimeMultiplayer.Sessions;

public interface ICoroutineHost
{
    void Run(IEnumerator routine);
}

public interface ISession : IDisposable
{
    SessionRole Role { get; }

    string Status { get; }

    bool IsFinished { get; }

    IReadOnlyList<RosterEntry> Roster { get; }

    int LocalPlayerId { get; }

    FamilyMode? FamilyMode { get; }

    bool TryGetRemotePlayerPosition(RosterEntry player, out Vector3 position);

    bool TryGetRemotePlayerYaw(RosterEntry player, out float yawDegrees);

    void Tick(double now);
}
