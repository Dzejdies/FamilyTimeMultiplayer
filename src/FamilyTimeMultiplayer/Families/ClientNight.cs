using System;
using System.Collections.Generic;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.UI;
using GAT;
using HarmonyLib;
using UnityEngine;

namespace FamilyTimeMultiplayer.Families;

public interface IClientBedSink
{
    void SendBedRequest(bool ready);
}

public static class ClientNight
{
    private static IClientBedSink? _sink;

    public static bool IsActive => _sink != null;

    public static bool IsReady { get; private set; }

    public static void Attach(IClientBedSink sink)
    {
        _sink = sink;
        IsReady = false;
    }

    public static void Detach()
    {
        _sink = null;
        IsReady = false;
        PlayerNotice.SetSticky(null);
    }

    public static void GoToBed()
    {
        RequireSink().SendBedRequest(true);
        IsReady = true;
        PlayerNotice.SetSticky("In bed. Waiting for the others...");
    }

    public static void GetUp()
    {
        RequireSink().SendBedRequest(false);
        IsReady = false;
        PlayerNotice.SetSticky(null);
    }

    public static void Apply(BedStatusMessage status)
    {
        IsReady = status.YouAreReady;
        string waiting = string.Join(", ", status.WaitingFor);
        PlayerNotice.SetSticky(status.WaitingFor.Count == 0 ? null : IsReady
            ? $"In bed. Waiting for: {waiting} (click the bed to get up)"
            : $"Others went to bed - waiting for: {waiting}");
    }

    public static void Play(NightOutcomeMessage outcome, Action<System.Collections.IEnumerator> run)
    {
        IsReady = false;
        PlayerNotice.SetSticky(null);
        run(PlayRoutine(outcome));
    }

    private static System.Collections.IEnumerator PlayRoutine(NightOutcomeMessage outcome)
    {
        FadeUI? fade = World.fadeUI;
        bool step = false;
        if (fade != null)
        {
            fade.FadeToBlack(() => step = true);
            while (!step)
            {
                yield return null;
            }
        }
        step = false;
        Movie2DManager? movies = World.movie2DManager;
        List<GameObject> list = MoviesFor(outcome);
        if (movies != null && list.Count > 0)
        {
            movies.PlayMovies(list, () => step = true);
            while (!step)
            {
                yield return null;
            }
        }
        step = false;
        GameFamilyAccess.AnimateGrowth(outcome.GrownBy, () => step = true);
        while (!step)
        {
            yield return null;
        }
        if (fade != null)
        {
            step = false;
            fade.FadeFromBlack(() => step = true);
            while (!step)
            {
                yield return null;
            }
        }
    }

    private static List<GameObject> MoviesFor(NightOutcomeMessage outcome)
    {
        var list = new List<GameObject>();
        BedManager? bed = UnityEngine.Object.FindFirstObjectByType<BedManager>();
        if (bed == null)
        {
            return list;
        }
        Traverse fields = Traverse.Create(bed);
        void Add(string field)
        {
            GameObject? prefab = fields.Field<GameObject>(field).Value;
            if (prefab != null)
            {
                list.Add(prefab);
            }
        }
        if (outcome.ChildrenMissing)
        {
            Add("missingChildPrefab");
        }
        if (outcome.ChildrenHungry)
        {
            Add("hungryChildPrefab");
        }
        Add(outcome.ChildrenMissing || outcome.ChildrenHungry ? "angryPrefab" : "happyChildPrefab");
        return list;
    }

    private static IClientBedSink RequireSink() =>
        _sink ?? throw new InvalidOperationException("No client session is attached for bed requests.");
}
