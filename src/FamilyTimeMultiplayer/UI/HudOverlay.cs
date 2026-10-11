using System;
using FamilyTimeMultiplayer.Commands;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Sessions;
using UnityEngine;

namespace FamilyTimeMultiplayer.UI;

public sealed class HudOverlay
{
    private const float NameplateHeightOffset = 2.1f;

    private readonly MultiplayerSettings _settings;
    private readonly FamilyRestorePrompt _familyRestore;

    public HudOverlay(MultiplayerSettings settings, MultiplayerDialogView dialog)
    {
        _settings = settings;
        _familyRestore = new FamilyRestorePrompt(dialog);
        Prompt = new TextPrompt(dialog);
    }

    public void Dispose() => _familyRestore.Dispose();

    public TextPrompt Prompt { get; }

    public void Update(ISession? session) => _familyRestore.Update(session);

    public void Draw(ISession? session)
    {
        if (session != null)
        {
            DrawNameplates(session);
            string text = $"{session.Role}: {session.Status}  [{_settings.ToggleWindowKeyEntry.Value}]";
            GUI.Label(new Rect(Screen.width - 520f, 8f, 510f, 22f), text);
        }
        string? notice = PlayerNotice.Current;
        if (notice != null)
        {
            GUI.Label(new Rect((Screen.width - 460f) / 2f, Screen.height * 0.62f, 460f, 30f), notice);
        }
    }

    private static void DrawNameplates(ISession session)
    {
        Camera? camera = Camera.main;
        if (camera == null)
        {
            return;
        }
        foreach (RosterEntry entry in session.Roster)
        {
            if (entry.PlayerId == session.LocalPlayerId || !session.TryGetRemotePlayerPosition(entry, out Vector3 world))
            {
                continue;
            }
            Vector3 screen = camera.WorldToScreenPoint(world + Vector3.up * NameplateHeightOffset);
            if (screen.z <= 0f)
            {
                continue;
            }
            GUI.Label(new Rect(screen.x - 80f, Screen.height - screen.y - 12f, 160f, 24f), entry.Name);
        }
    }

}
