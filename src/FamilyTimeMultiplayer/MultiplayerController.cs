using System;
using System.Collections;
using BepInEx.Logging;
using FamilyTimeMultiplayer.Avatars;
using FamilyTimeMultiplayer.Core.Launch;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Core.Saves;
using FamilyTimeMultiplayer.Diagnostics;
using FamilyTimeMultiplayer.Game;
using FamilyTimeMultiplayer.Sessions;
using FamilyTimeMultiplayer.UI;
using UnityEngine;

namespace FamilyTimeMultiplayer;

public sealed class MultiplayerController : MonoBehaviour, ICoroutineHost, IMultiplayerMenuActions
{
    private ManualLogSource _log = null!;
    private MultiplayerSettings _settings = null!;
    private LaunchOptions _launch = null!;
    private SaveCollectionService _saveCollections = null!;
    private HudOverlay _hud = null!;
    private SaveImportFlow _saveImport = null!;
    private bool _startupMenuShown;
    private bool _openPagePending;
    private Action? _actionAfterWorldReady;
    private Action? _nativeWorldEntryAction;
    private bool _nativeWorldEntryStarted;
    private RemoteAvatarPresenter _avatars = null!;
    private SessionProbe? _probe;
    private ScenarioRunner? _scenario;
    private bool _launchActionPending;
    private bool _launchMapPending;
    private bool _launchMapBusy;
    private bool _gameRunsInBackground;
    private bool _capturedBackgroundState;

    public static MultiplayerController? Current { get; private set; }

    private static readonly System.Reflection.MethodInfo TogglePauseMenu =
        HarmonyLib.AccessTools.Method(typeof(GAT.PlayerController), "TogglePauseMenu")
        ?? throw new MissingMethodException(nameof(GAT.PlayerController), "TogglePauseMenu");

    public ISession? Session { get; private set; }

    public MultiplayerSettings Settings => _settings;

    public bool RequiresWorldSelection => GameApiCompatibility.HasNativeTitleFlow && !LocalPlayer.IsWorldReady;

    public string? LastError { get; private set; }

    internal void Initialize(ManualLogSource log, MultiplayerSettings settings, LaunchOptions launch, SaveCollectionService saveCollections)
    {
        _log = log;
        _settings = settings;
        _launch = launch;
        _saveCollections = saveCollections;
        MultiplayerDialogView dialog = gameObject.AddComponent<MultiplayerDialogView>();
        _hud = new HudOverlay(settings, dialog);
        _saveImport = new SaveImportFlow(dialog, new NativeSaveCatalog(saveCollections, log), saveCollections, log);
        Current = this;
        if (WillShowStartupMenu())
        {
            StartupGate.Hold();
        }
        _avatars = new RemoteAvatarPresenter(log);
        _launchActionPending = launch.Action != LaunchAction.None;
        _launchMapPending = launch.LoadMap != null || launch.SaveMap != null;
        if (launch.UsesMultiplayerSaveCollection) SelectSaveCollection(SaveCollection.Multiplayer);
        _gameRunsInBackground = FamilyTimeMultiplayer.Game.GameApiCompatibility.RunInBackground;
        _capturedBackgroundState = true;
        if (launch.RelayAddress != null)
        {
            settings.OverrideRelayForThisRun(launch.RelayAddress);
        }
        if (launch.PlayerName != null)
        {
            settings.OverridePlayerNameForThisRun(launch.PlayerName);
        }
        if (launch.Scenario != null)
        {
            _scenario = ScenarioRunner.Create(launch.Scenario, log);
            _probe = new SessionProbe(log);
        }
    }

    public void Run(IEnumerator routine) => StartCoroutine(routine);

    public void StartHosting() => StartHosting(_settings.FamilyModeEntry.Value);

    public void StartHosting(FamilyMode familyMode)
    {
        SelectSaveCollection(SaveCollection.Multiplayer);
        RequireNoSession();
        if (!LocalPlayer.IsWorldReady)
        {
            throw new InvalidOperationException("The world is still loading; host once you are in game.");
        }
        Session = new HostSession(_log, _settings, this, familyMode);
        _settings.FamilyModeEntry.Value = familyMode;
        LastError = null;
    }

    public void Join(string address)
    {
        SelectSaveCollection(SaveCollection.Multiplayer);
        RequireNoSession();
        if (!LocalPlayer.IsWorldReady)
        {
            throw new InvalidOperationException("The world is still loading; join once you are in game.");
        }
        Core.Transport.JoinTarget target = Core.Transport.JoinTarget.Parse(address, _settings.RelayAddress);
        _settings.LastJoinAddressEntry.Value = address.Trim();
        Session = new ClientSession(_log, _settings, target);
        LastError = null;
    }

    public void Leave()
    {
        ISession? session = Session;
        Session = null;
        if (session == null)
        {
            return;
        }
        try
        {
            session.Dispose();
        }
        catch (Exception exception)
        {
            _log.LogError($"[Session] closing the {session.Role} session failed part-way: {exception}");
            LastError = $"Session closed with an error: {exception.Message}";
        }
        finally
        {
            ReplicationGate.Leave();
            if (Commands.CommanderContext.Active != null)
            {
                _log.LogError($"[Session] commander context of player #{Commands.CommanderContext.Active.PlayerId} was still active after leaving; cleared");
                Commands.CommanderContext.ResetAfterSession();
            }
        }
    }

    private void RequireNoSession()
    {
        if (Session != null)
        {
            throw new InvalidOperationException($"Already in a {Session.Role} session; leave it first.");
        }
    }

    private void Update()
    {
        double now = FamilyTimeMultiplayer.Game.GameClock.Now;
        HandleMenuKey();
        _hud.Update(Session);
        _saveImport.Tick();
        ShowStartupMenuOnce();
        TrackNativeWorldEntry();
        OpenPendingPage();
        RunPendingLaunchAction();
        if (Session != null)
        {
            try
            {
                Session.Tick(now);
            }
            catch (Exception exception)
            {
                _log.LogError($"[Session] {Session.Role} session crashed and was stopped: {exception}");
                LastError = $"Session stopped after an error: {exception.Message}";
                Leave();
            }
            if (Session != null && Session.IsFinished)
            {
                LastError = Session.Status;
                Session = null;
            }
        }
        FamilyTimeMultiplayer.Game.GameApiCompatibility.RunInBackground = Session != null || _gameRunsInBackground;
        if (!_launchMapPending)
        {
            _scenario?.Tick(now, Session);
        }
        _probe?.Tick(now, Session);
    }

    private void RunPendingLaunchMapOperation()
    {
        if (!_launchMapPending || _launchMapBusy || !LocalPlayer.IsWorldReady)
        {
            return;
        }
        _launchMapBusy = true;
        GAT.WorldSerializer serializer = GatInterop.RequireAlive(GAT.World.worldSerializer, "World.worldSerializer is missing.");
        void Done(string operation, bool success)
        {
            _log.LogInfo($"[Launch] {operation} finished: {(success ? "ok" : "FAILED")}");
            _launchMapBusy = false;
            _launchMapPending = false;
        }
        if (_launch.LoadMap != null)
        {
            serializer.Load(_launch.LoadMap, success => Done($"load '{_launch.LoadMap}'", success));
        }
        else
        {
            serializer.Save(_launch.SaveMap!, success => Done($"save '{_launch.SaveMap}'", success));
        }
    }

    private void RunPendingLaunchAction()
    {
        RunPendingLaunchMapOperation();
        if (!_launchActionPending || _launchMapPending || !LocalPlayer.IsWorldReady)
        {
            return;
        }
        _launchActionPending = false;
        try
        {
            if (_launch.Action == LaunchAction.Host)
            {
                if (_launch.Port.HasValue)
                {
                    _settings.PortEntry.Value = _launch.Port.Value;
                }
                StartHosting(_launch.FamilyMode ?? _settings.FamilyModeEntry.Value);
            }
            else
            {
                Join(_launch.JoinText!);
            }
        }
        catch (Exception exception)
        {
            _log.LogError($"[Launch] {_launch.Action} from the command line failed: {exception}");
            LastError = exception.Message;
        }
    }

    private void LateUpdate() => _avatars.Present(Session, Time.deltaTime);

    private void OnGUI() => _hud.Draw(Session);

    public void HostWith(FamilyMode mode)
    {
        SelectSaveCollection(SaveCollection.Multiplayer);
        CloseBook();
        RunWhenWorldReady(() => StartHosting(mode));
    }

    public void OpenHostWorld(FamilyMode mode, bool loadSavedWorld)
    {
        SelectSaveCollection(SaveCollection.Multiplayer);
        RunMenuAction(() => OpenNativeWorld(loadSavedWorld, () => StartHosting(mode)));
    }

    public void OpenJoinWorld(string address, bool loadSavedWorld)
    {
        SelectSaveCollection(SaveCollection.Multiplayer);
        RunMenuAction(() => OpenNativeWorld(loadSavedWorld, () => Join(address)));
    }

    public void SelectSaveCollection(SaveCollection collection)
    {
        _saveCollections.Select(collection);
        _log.LogInfo($"[Saves] selected {collection} collection");
    }

    public void PromptAndJoin() =>
        _hud.Prompt.Open("Join: room code (e.g. K7Q-M2X) or ip / ip:port", _settings.LastJoinAddressEntry.Value, 64, value =>
            RunMenuAction(() =>
            {
                Core.Transport.JoinTarget.Parse(value, _settings.RelayAddress);
                _settings.LastJoinAddressEntry.Value = value;
                if (GameApiCompatibility.HasNativeTitleFlow && !LocalPlayer.IsWorldReady)
                {
                    PauseBookMultiplayerPage menu = GetMultiplayerPage()
                        ?? throw new InvalidOperationException("The Multiplayer menu is no longer available.");
                    menu.ChooseJoinWorld(value);
                    return;
                }
                CloseBook();
                RunWhenWorldReady(() => Join(value));
            }));

    public TextPrompt JoinPrompt => _hud.Prompt;

    public void LeaveSession() => Leave();

    public void PromptForJoinAddress() =>
        _hud.Prompt.Open("Host address (ip, ip:port) or room code", _settings.LastJoinAddressEntry.Value, 64, value =>
            RunMenuAction(() =>
            {
                Core.Transport.JoinTarget.Parse(value, _settings.RelayAddress);
                _settings.LastJoinAddressEntry.Value = value.Trim();
            }));

    public void PromptForRelayAddress() =>
        _hud.Prompt.Open("Relay server (host or host:port, empty = off)", _settings.RelayAddressEntry.Value, 96, value =>
            RunMenuAction(() =>
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    LaunchOptions.ParseEndpoint(value);
                }
                _settings.RelayAddressEntry.Value = value.Trim();
            }));

    public void PromptForPlayerName() =>
        _hud.Prompt.Open("Your name", _settings.PlayerName, 24, value => _settings.PlayerNameEntry.Value = value);

    public void OpenModFolder()
    {
        string folder = BepInEx.Paths.ConfigPath;
        _log.LogInfo($"[Menu] opening mod folder {folder}");
        Application.OpenURL(new Uri(folder).AbsoluteUri);
    }

    public void OpenSaveImport()
    {
        RunMenuAction(() => _saveImport.Open());
    }

    private void RunMenuAction(Action action)
    {
        try
        {
            action();
            LastError = null;
        }
        catch (Exception exception)
        {
            LastError = exception.Message;
            _log.LogWarning($"[Menu] {exception.Message}");
        }
    }

    private void HandleMenuKey()
    {
        UnityEngine.InputSystem.Keyboard? keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard == null || _hud.Prompt.IsOpen || !keyboard[_settings.ToggleWindowKeyEntry.Value].wasPressedThisFrame)
        {
            return;
        }
        GAT.PauseMenuBook? book = GAT.PauseMenuBook.ActiveInstance;
        PauseBookMultiplayerPage? page = book != null ? book.GetComponent<PauseBookMultiplayerPage>() : null;
        if (book != null && !book.IsClosing && page != null && page.IsShowing)
        {
            book.Close();
            return;
        }
        RequestMultiplayerPage();
    }

    private bool WillShowStartupMenu() =>
        Core.Launch.StartupMenuPolicy.ShouldShowStartupMenu(GameApiCompatibility.HasNativeTitleFlow, _settings.ShowMenuOnStartupEntry.Value,
            _launch.Action == LaunchAction.None && (_launch.Scenario == null || _launch.Scenario == "menu-shot") && _launch.LoadMap == null && _launch.SaveMap == null);

    private void OpenNativeWorld(bool loadSavedWorld, Action afterWorldReady)
    {
        if (!RequiresWorldSelection)
        {
            CloseBook();
            RunWhenWorldReady(afterWorldReady);
            return;
        }
        _nativeWorldEntryAction = afterWorldReady;
        _nativeWorldEntryStarted = false;
        GetMultiplayerPage()?.ReturnToNativeWorldSelection();
        GAT.PauseMenuBook? book = GAT.PauseMenuBook.ActiveInstance;
        if (book == null)
        {
            _nativeWorldEntryAction = null;
            throw new InvalidOperationException("The native world menu is no longer available.");
        }
        try
        {
            if (loadSavedWorld) GameApiCompatibility.OpenNativeSaveLoad(book);
            else GameApiCompatibility.OpenNativeNewGame(book);
        }
        catch (Exception exception)
        {
            CancelNativeWorldEntry();
            throw new InvalidOperationException("Could not open the native world selection page.", exception);
        }
    }

    private static PauseBookMultiplayerPage? GetMultiplayerPage()
    {
        GAT.PauseMenuBook? book = GAT.PauseMenuBook.ActiveInstance;
        return book == null ? null : book.GetComponent<PauseBookMultiplayerPage>();
    }

    private void TrackNativeWorldEntry()
    {
        if (_nativeWorldEntryAction == null) return;
        if (GameApiCompatibility.NativeSessionIsStartingOrLoading)
        {
            _nativeWorldEntryStarted = true;
            return;
        }
        if (!_nativeWorldEntryStarted)
        {
            GAT.PauseMenuBook? book = GAT.PauseMenuBook.ActiveInstance;
            if (book != null && GameApiCompatibility.IsNativeMainMenuActive(book)) CancelNativeWorldEntry();
            return;
        }
        if (GameApiCompatibility.NativeSessionStateName == "Playing")
        {
            _actionAfterWorldReady = _nativeWorldEntryAction;
        }
        _nativeWorldEntryAction = null;
        _nativeWorldEntryStarted = false;
    }

    public void CancelNativeWorldEntry()
    {
        _nativeWorldEntryAction = null;
        _nativeWorldEntryStarted = false;
    }

    private void ShowStartupMenuOnce()
    {
        if (!_startupMenuShown && StartupGate.IsAwaitingChoice && GAT.World.playerController != null && Camera.main != null)
        {
            _startupMenuShown = true;
            RequestMultiplayerPage();
        }
        DetectStartupChoice();
        RunActionAfterWorldReady();
        ReleaseTutorialWhenSettled();
    }

    private void DetectStartupChoice()
    {
        if (!StartupGate.IsAwaitingChoice || !_startupMenuShown || _openPagePending)
        {
            return;
        }
        GAT.PauseMenuBook? book = GAT.PauseMenuBook.ActiveInstance;
        if (book == null || book.IsClosing)
        {
            StartupGate.ChoiceMade();
            _log.LogInfo("[Menu] startup choice made; generating the world");
        }
    }

    private void RunActionAfterWorldReady()
    {
        if (_actionAfterWorldReady == null || !LocalPlayer.IsWorldReady)
        {
            return;
        }
        Action action = _actionAfterWorldReady;
        _actionAfterWorldReady = null;
        RunMenuAction(action);
    }

    private void ReleaseTutorialWhenSettled()
    {
        if (!StartupGate.IsHolding || StartupGate.IsAwaitingChoice || _actionAfterWorldReady != null || !LocalPlayer.IsWorldReady)
        {
            return;
        }
        bool joining = Session is ClientSession client && !client.IsLive && !client.IsFinished;
        if (!joining)
        {
            StartupGate.Release();
            _log.LogInfo("[Menu] world ready; tutorial prompt released");
        }
    }

    private void RunWhenWorldReady(Action action)
    {
        if (LocalPlayer.IsWorldReady)
        {
            RunMenuAction(action);
            return;
        }
        _actionAfterWorldReady = action;
    }

    private static void CloseBook()
    {
        GAT.PauseMenuBook? book = GAT.PauseMenuBook.ActiveInstance;
        if (book != null && !book.IsClosing)
        {
            book.Close();
        }
    }

    public void RequestMultiplayerPage()
    {
        GAT.PlayerController? player = GAT.World.playerController;
        if (player == null)
        {
            return;
        }
        GAT.PauseMenuBook? book = GAT.PauseMenuBook.ActiveInstance;
        if (book == null || book.IsClosing)
        {
            TogglePauseMenu.Invoke(player, null);
        }
        _openPagePending = true;
    }

    public void OpenMultiplayerFromTitle()
    {
        SelectSaveCollection(SaveCollection.Multiplayer);
        _openPagePending = true;
    }

    private void OpenPendingPage()
    {
        if (!_openPagePending)
        {
            return;
        }
        GAT.PauseMenuBook? book = GAT.PauseMenuBook.ActiveInstance;
        PauseBookMultiplayerPage? page = book != null ? book.GetComponent<PauseBookMultiplayerPage>() : null;
        if (page == null)
        {
            return;
        }
        _openPagePending = false;
        page.Show();
    }

    private void OnDestroy()
    {
        TryCleanup("session", Leave);
        TryCleanup("startup gate", StartupGate.Release);
        TryCleanup("startup pause scope", Patches.WorldGenerationGatePatch.ReleasePendingPauseScope);
        TryCleanup("diagnostic scenario", () => _scenario?.Dispose());
        if (_capturedBackgroundState)
        {
            TryCleanup("background execution state", () => FamilyTimeMultiplayer.Game.GameApiCompatibility.RunInBackground = _gameRunsInBackground);
        }
        TryCleanup("HUD", () => _hud?.Dispose());
        _scenario = null;
        if (Current == this)
        {
            Current = null;
        }
    }

    private void OnApplicationQuit()
    {
        TryCleanup("session", Leave);
        TryCleanup("startup gate", StartupGate.Release);
        TryCleanup("startup pause scope", Patches.WorldGenerationGatePatch.ReleasePendingPauseScope);
        TryCleanup("diagnostic scenario", () => _scenario?.Dispose());
        if (_capturedBackgroundState)
        {
            TryCleanup("background execution state", () => FamilyTimeMultiplayer.Game.GameApiCompatibility.RunInBackground = _gameRunsInBackground);
        }
    }

    private void TryCleanup(string owner, Action cleanup)
    {
        try
        {
            cleanup();
        }
        catch (Exception exception)
        {
            _log?.LogError($"[Cleanup] {owner} cleanup failed: {exception}");
        }
    }
}
