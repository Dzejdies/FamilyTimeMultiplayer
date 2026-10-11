using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Game;
using GAT;
using HarmonyLib;

namespace FamilyTimeMultiplayer.Commands;

public static class CommandAudioCatalog
{
    private static readonly (CommandSound Sound, string Field)[] Entries =
    {
        (CommandSound.Start, "commandStartAudioGroup"),
        (CommandSound.Stop, "commandStopAudioGroup"),
        (CommandSound.DropOff, "dropOffStartAudioGroup"),
        (CommandSound.QueueAdd, "queueAddAudioGroup"),
        (CommandSound.QueueRemove, "queueRemoveAudioGroup"),
        (CommandSound.Construction, "commandConstructionAudioGroup"),
        (CommandSound.Attack, "commandAttackAudioGroup"),
        (CommandSound.Station, "commandStationAudioGroup"),
        (CommandSound.FollowStart, "followStartAudioGroup"),
        (CommandSound.FollowStop, "followEndAudioGroup"),
        (CommandSound.Lock, "lockAudioGroup"),
        (CommandSound.Unlock, "unlockAudioGroup"),
    };

    private static readonly Dictionary<CommandSound, AccessTools.FieldRef<CommandService, AudioGroup>> Fields =
        Entries.Where(entry => entry.Sound != CommandSound.Station || typeof(CommandService).GetField(entry.Field, BindingFlags.Instance | BindingFlags.NonPublic) != null)
            .ToDictionary(entry => entry.Sound, entry => AccessTools.FieldRefAccess<CommandService, AudioGroup>(entry.Field));

    private static readonly MethodInfo NativePlay =
        AccessTools.Method(AccessTools.TypeByName("GAT.CommandAudio") ?? throw new TypeLoadException("GAT.CommandAudio not found"), "Play")
        ?? throw new MissingMethodException("GAT.CommandAudio", "Play");

    public static AudioGroup Get(CommandSound sound)
    {
        if (!Fields.TryGetValue(sound, out AccessTools.FieldRef<CommandService, AudioGroup> field))
        {
            if (sound == CommandSound.Station) throw new MissingFieldException(nameof(CommandService), "commandStationAudioGroup");
            throw new ArgumentOutOfRangeException(nameof(sound), sound, "Unknown command sound.");
        }
        CommandService commands = GatInterop.RequireAlive(World.commandService, "World.commandService is missing.");
        return GatInterop.RequireAlive(field(commands), $"Command audio group {sound} is missing.");
    }

    public static CommandSound Identify(AudioGroup group)
    {
        foreach (CommandSound sound in Fields.Keys)
        {
            if (Get(sound) == group)
            {
                return sound;
            }
        }
        throw new InvalidOperationException($"Audio group '{group.name}' is not a configured command sound.");
    }

    public static void Play(CommandSound sound) =>
        GatInterop.RequireAlive(World.soundManager, "World.soundManager is missing.").PlayAudioGroup(Get(sound));

    public static void PlayNative(CommandSound sound) => NativePlay.Invoke(null, new object[] { Get(sound) });

    public static CommandSound ForLockState(bool locked) => locked ? CommandSound.Lock : CommandSound.Unlock;
}
