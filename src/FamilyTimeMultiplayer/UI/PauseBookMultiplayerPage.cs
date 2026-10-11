using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using FamilyTimeMultiplayer.Core.Saves;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Game;
using FamilyTimeMultiplayer.Sessions;
using GAT;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FamilyTimeMultiplayer.UI;

public interface IMultiplayerMenuActions
{
    ISession? Session { get; }

    string? LastError { get; }

    MultiplayerSettings Settings { get; }

    bool RequiresWorldSelection { get; }

    void HostWith(FamilyMode mode);

    void OpenHostWorld(FamilyMode mode, bool loadSavedWorld);

    void OpenJoinWorld(string address, bool loadSavedWorld);

    void CancelNativeWorldEntry();

    void PromptAndJoin();

    void LeaveSession();

    void PromptForJoinAddress();

    void PromptForRelayAddress();

    void PromptForPlayerName();

    void OpenModFolder();

    void SelectSaveCollection(SaveCollection collection);

    void OpenSaveImport();
}

public sealed class PauseBookMultiplayerPage : MonoBehaviour
{
    private const string EntryButtonName = "Multiplayer";
    private const float CaptionRefreshSeconds = 0.25f;

    private static readonly MethodInfo ShowPageMethod =
        AccessTools.Method(typeof(PauseMenuBook), "ShowPage")
        ?? throw new MissingMethodException(nameof(PauseMenuBook), "ShowPage");

    private readonly List<GameObject> _dynamicButtons = new List<GameObject>();
    private PauseMenuBook _book = null!;
    private IMultiplayerMenuActions _actions = null!;
    private GameObject _page = null!;
    private Transform _buttonColumn = null!;
    private GameObject _buttonTemplate = null!;
    private TextMeshProUGUI _caption = null!;
    private string _builtStateKey = string.Empty;
    private bool _settingsOpen;
    private bool _waitingForNativePageReturn;
    private FamilyMode? _selectedHostMode;
    private string? _selectedJoinAddress;
    private float _nextCaptionRefresh;

    public static PauseBookMultiplayerPage Install(PauseMenuBook book, IMultiplayerMenuActions actions)
    {
        PauseBookMultiplayerPage page = book.gameObject.AddComponent<PauseBookMultiplayerPage>();
        page.Build(book, actions);
        return page;
    }

    public void Show()
    {
        _settingsOpen = false;
        ShowPageMethod.Invoke(_book, new object[] { _page });
        Rebuild();
    }

    public bool IsShowing => _page != null && _page.activeSelf;

    private void Build(PauseMenuBook book, IMultiplayerMenuActions actions)
    {
        _book = book;
        _actions = actions;
        GameObject mainMenu = Traverse.Create(book).Field<GameObject>("mainMenu").Value
            ?? throw new MissingFieldException(nameof(PauseMenuBook), "mainMenu");
        Transform mainRight = RequireChild(mainMenu.transform, "Right Page");
        Transform graphicsButton = RequireChild(mainRight, "Graphics");

        Transform entry = Instantiate(graphicsButton, mainRight);
        entry.name = EntryButtonName;
        Transform? tutorial = mainRight.Find("Tutorials");
        GameApiCompatibility.SetSiblingIndex(entry, GameApiCompatibility.GetSiblingIndex(tutorial ?? graphicsButton) + 1);
        Configure(entry.gameObject, "Multiplayer", Show, interactable: true);

        _page = Instantiate(mainMenu, mainMenu.transform.parent);
        _page.name = "MultiplayerPage";
        _page.SetActive(false);
        Transform left = RequireChild(_page.transform, "Left Page");
        RequireChild(left, "Title").gameObject.SetActive(false);
        _caption = RequireChild(left, "Caption").GetComponent<TextMeshProUGUI>()
            ?? throw new InvalidOperationException("Left Page/Caption has no TextMeshProUGUI.");
        _caption.fontSize *= 0.85f;
        DestroyChildIfPresent(left, "Controls");
        DestroyChildIfPresent(left, "Logo");

        _buttonColumn = RequireChild(_page.transform, "Right Page");
        SetText(RequireChild(_buttonColumn, "Title"), "Play together");
        _buttonTemplate = RequireChild(_buttonColumn, "Graphics").gameObject;
        _buttonTemplate.name = "ButtonTemplate";
        _buttonTemplate.SetActive(false);
        var nativeButtons = new List<GameObject>();
        foreach (Transform child in _buttonColumn)
        {
            if (child.name != "Title" && child.gameObject != _buttonTemplate)
            {
                nativeButtons.Add(child.gameObject);
            }
        }
        foreach (GameObject nativeButton in nativeButtons) DestroyImmediate(nativeButton);
    }

    private void Update()
    {
        if (!IsShowing)
        {
            return;
        }
        if (_waitingForNativePageReturn)
        {
            _waitingForNativePageReturn = false;
            ReturnToNativeWorldSelection();
            _actions.CancelNativeWorldEntry();
        }
        string key = StateKey();
        if (key != _builtStateKey)
        {
            Rebuild();
        }
        if (Time.unscaledTime >= _nextCaptionRefresh)
        {
            _nextCaptionRefresh = Time.unscaledTime + CaptionRefreshSeconds;
            _caption.text = DescribeState();
        }
    }

    private string StateKey()
    {
        ISession? session = _actions.Session;
        MultiplayerSettings settings = _actions.Settings;
        return $"{session?.Role}|{_settingsOpen}|{_selectedHostMode}|{_selectedJoinAddress}|{settings.LastJoinAddressEntry.Value}|{settings.PlayerName}";
    }

    private void Rebuild()
    {
        foreach (GameObject button in _dynamicButtons)
        {
            Destroy(button);
        }
        _dynamicButtons.Clear();
        _builtStateKey = StateKey();
        if (_selectedHostMode.HasValue || _selectedJoinAddress != null)
        {
            AddButton("New world", () => OpenSelectedWorld(loadSavedWorld: false));
            AddButton("Load saved world", () => OpenSelectedWorld(loadSavedWorld: true));
            AddButton("Back", CancelWorldSelection);
        }
        else if (_settingsOpen)
        {
            AddButton("Join address", _actions.PromptForJoinAddress);
            AddButton("Relay server", _actions.PromptForRelayAddress);
            AddButton("Your name", _actions.PromptForPlayerName);
            AddButton("Mod folder", _actions.OpenModFolder);
            AddButton("Back", () => _settingsOpen = false);
        }
        else if (_actions.Session == null)
        {
            if (!_actions.RequiresWorldSelection) AddButton("Play solo", ReturnToSoloCollection);
            AddButton("Import save copy", _actions.OpenSaveImport);
            AddButton("Host shared", () => ChooseHostWorld(FamilyMode.Shared));
            AddButton("Host separate", () => ChooseHostWorld(FamilyMode.Separate));
            AddButton("Join", _actions.PromptAndJoin);
            AddButton("Settings", () => _settingsOpen = true);
            AddButton("Back", () => _book.Back());
        }
        else
        {
            AddButton(_actions.Session.Role == Game.SessionRole.Host ? "Stop hosting" : "Leave", _actions.LeaveSession);
            AddButton("Settings", () => _settingsOpen = true);
            AddButton("Back", () => _book.Back());
        }
        _caption.text = DescribeState();
    }

    public void ChooseHostWorld(FamilyMode mode)
    {
        if (!_actions.RequiresWorldSelection)
        {
            _actions.HostWith(mode);
            return;
        }
        _selectedHostMode = mode;
        _selectedJoinAddress = null;
        Rebuild();
    }

    public void ChooseJoinWorld(string address)
    {
        if (!_actions.RequiresWorldSelection)
        {
            _selectedJoinAddress = address;
            _actions.OpenJoinWorld(address, loadSavedWorld: false);
            return;
        }
        _selectedHostMode = null;
        _selectedJoinAddress = address;
        Rebuild();
    }

    public void ReturnToNativeWorldSelection()
    {
        _selectedHostMode = null;
        _selectedJoinAddress = null;
    }

    private void OpenSelectedWorld(bool loadSavedWorld)
    {
        if (_selectedHostMode is FamilyMode mode)
        {
            _waitingForNativePageReturn = true;
            _actions.OpenHostWorld(mode, loadSavedWorld);
        }
        else if (_selectedJoinAddress is string address)
        {
            _waitingForNativePageReturn = true;
            _actions.OpenJoinWorld(address, loadSavedWorld);
        }
    }

    private void ReturnToSoloCollection()
    {
        _actions.SelectSaveCollection(SaveCollection.Solo);
        _book.Close();
    }

    private void CancelWorldSelection()
    {
        _selectedHostMode = null;
        _selectedJoinAddress = null;
        Rebuild();
    }

    private string DescribeState()
    {
        var text = new StringBuilder("<size=170%>Multiplayer</size>\n\n");
        ISession? session = _actions.Session;
        MultiplayerSettings settings = _actions.Settings;
        if (_settingsOpen)
        {
            text.AppendLine($"Join address: {settings.LastJoinAddressEntry.Value}");
            text.AppendLine($"Your name: {settings.PlayerName}");
            text.AppendLine($"Hosting port: {settings.PortEntry.Value}");
            text.AppendLine($"Relay: {(string.IsNullOrWhiteSpace(settings.RelayAddress) ? "off" : settings.RelayAddress)}");
            text.AppendLine("More options live in the mod folder's .cfg file.");
        }
        else if (session == null)
        {
            if (_selectedHostMode is FamilyMode selectedMode) text.AppendLine($"Hosting a {selectedMode} family. Choose a world.");
            else if (_selectedJoinAddress != null) text.AppendLine($"Joining {_selectedJoinAddress}. Choose a world to replace with the host's save.");
            else
            {
                text.AppendLine($"You: {settings.PlayerName}");
                text.AppendLine("Shared: one family for all.");
                text.AppendLine("Separate: own kids per player.");
                text.AppendLine("Join: type the host's room code.");
            }
        }
        else
        {
            text.AppendLine(session.Status);
            if (session is HostSession host && host.RoomCode != null)
            {
                text.AppendLine($"<size=150%>Room code: {host.RoomCode}</size>");
            }
            if (session.FamilyMode.HasValue)
            {
                text.AppendLine($"Family mode: {session.FamilyMode.Value}");
            }
            text.AppendLine();
            foreach (RosterEntry player in session.Roster)
            {
                string tag = player.PlayerId == PlayerRosterMessage.HostPlayerId ? " (host)" : string.Empty;
                string you = player.PlayerId == session.LocalPlayerId ? " - you" : string.Empty;
                text.AppendLine($"- {player.Name}{tag}{you}");
            }
        }
        if (_actions.LastError != null)
        {
            text.AppendLine();
            text.AppendLine(_actions.LastError);
        }
        return text.ToString();
    }

    private void AddButton(string label, Action onClick, bool interactable = true)
    {
        GameObject button = Instantiate(_buttonTemplate, _buttonColumn);
        button.name = label;
        button.SetActive(true);
        Configure(button, label, onClick, interactable);
        FitSingleLine(button);
        _dynamicButtons.Add(button);
    }

    private static void Configure(GameObject buttonObject, string label, Action onClick, bool interactable)
    {
        SetText(buttonObject.transform, label);
        Button button = buttonObject.GetComponent<Button>()
            ?? throw new InvalidOperationException($"'{buttonObject.name}' has no Button.");
        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(() => onClick());
        button.interactable = interactable;
    }

    private static void SetText(Transform node, string text)
    {
        TextMeshProUGUI label = node.GetComponentInChildren<TextMeshProUGUI>(includeInactive: true)
            ?? throw new InvalidOperationException($"'{node.name}' has no TextMeshProUGUI.");
        label.text = text;
    }

    private static void FitSingleLine(GameObject buttonObject)
    {
        TextMeshProUGUI label = buttonObject.GetComponentInChildren<TextMeshProUGUI>(includeInactive: true);
        if (label == null)
        {
            return;
        }
        float size = label.fontSize;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.enableAutoSizing = true;
        label.fontSizeMax = size;
        label.fontSizeMin = size * 0.55f;
    }

    private static Transform RequireChild(Transform parent, string name) =>
        parent.Find(name) ?? throw new MissingReferenceException($"Pause book is missing '{parent.name}/{name}'; the game layout changed.");

    private static void DestroyChildIfPresent(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child != null)
        {
            DestroyImmediate(child.gameObject);
        }
    }
}
