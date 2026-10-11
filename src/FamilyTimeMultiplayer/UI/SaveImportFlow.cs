using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BepInEx.Logging;
using FamilyTimeMultiplayer.Core.Saves;
using FamilyTimeMultiplayer.Game;
using FamilyTimeMultiplayer.Patches;

namespace FamilyTimeMultiplayer.UI;

internal sealed class SaveImportFlow
{
    private const int MaximumSaveNameLength = 64;
    private readonly MultiplayerDialogView _view;
    private readonly NativeSaveCatalog _catalog;
    private readonly SaveCollectionService _saveCollections;
    private readonly ManualLogSource _log;
    private Task<IReadOnlyList<SaveImportEntry>>? _loadTask;
    private Task<string>? _copyTask;
    private SaveImportEntry? _source;
    private SaveCollection? _destination;

    public SaveImportFlow(MultiplayerDialogView view, NativeSaveCatalog catalog, SaveCollectionService saveCollections, ManualLogSource log)
    {
        _view = view;
        _catalog = catalog;
        _saveCollections = saveCollections;
        _log = log;
    }

    public void Open()
    {
        if (_copyTask != null) return;
        if (_loadTask != null)
        {
            ShowLoading();
            return;
        }
        _source = null;
        _destination = null;
        _loadTask = Task.Run(_catalog.LoadEntries);
        ShowLoading();
    }

    private void ShowLoading()
    {
        _view.ShowChoices(this, "Import a save copy", "Reading saves from Solo and Multiplayer…", CancelButton());
    }

    public void Tick()
    {
        if (_loadTask?.IsCompleted == true) CompleteLoad();
        if (_copyTask?.IsCompleted == true) CompleteCopy();
    }

    public void Cancel()
    {
        if (_copyTask != null) return;
        _source = null;
        _destination = null;
        _view.Hide(this);
    }

    public void SelectSource(SaveImportEntry source)
    {
        _source = source;
        ShowDestinationChoices();
    }

    public void SelectDestination(SaveCollection destination)
    {
        _destination = destination;
        PromptForName(_source?.Name ?? string.Empty);
    }

    public void Copy(string requestedName)
    {
        SaveImportEntry source = _source ?? throw new InvalidOperationException("Select a source save before copying.");
        SaveCollection destination = _destination ?? throw new InvalidOperationException("Select a destination collection before copying.");
        string directory = _saveCollections.GetDirectory(destination);
        string extension = _saveCollections.GetNativeSaveExtension();
        _copyTask = Task.Run(() => SaveImport.CopyToUniqueName(source.Path, directory, requestedName, extension));
        _view.ShowChoices(this, "Copying save", "The source save is kept unchanged. Please wait…", new DialogButton("Working…", () => { }));
    }

    private void CompleteLoad()
    {
        Task<IReadOnlyList<SaveImportEntry>> task = _loadTask!;
        _loadTask = null;
        if (!_view.IsOwnedBy(this))
        {
            if (task.IsFaulted) _ = task.Exception;
            return;
        }
        if (task.IsFaulted)
        {
            ShowLoadFailure(task.Exception?.GetBaseException() ?? new IOException("Could not read save entries."));
            return;
        }
        _log.LogInfo($"[Saves] found {task.Result.Count} importable saves across both collections");
        ShowSourceChoices(task.Result);
    }

    private void CompleteCopy()
    {
        Task<string> task = _copyTask!;
        _copyTask = null;
        bool ownsDialog = _view.IsOwnedBy(this);
        if (task.IsFaulted)
        {
            Exception failure = task.Exception?.GetBaseException() ?? new IOException("Could not copy the save.");
            if (ownsDialog) ShowCopyFailure(failure);
            else _log.LogWarning($"[Saves] background copy failed after the import dialog closed: {failure.Message}");
            return;
        }
        string destinationPath = task.Result;
        RefreshNativeMenu(destinationPath);
        _log.LogInfo($"[Saves] copied '{_source?.Name}' from {_source?.Collection} to {_destination}");
        if (ownsDialog)
            _view.ShowChoices(this, "Copy complete", $"A copy was created as {Path.GetFileNameWithoutExtension(destinationPath)} in {_destination}.",
                new DialogButton("Done", Cancel));
    }

    private void RefreshNativeMenu(string destinationPath)
    {
        if (_destination != _saveCollections.SelectedCollection) return;
        try
        {
            NativeSaveMenuRefresh.RefreshIfActive(Path.GetFileNameWithoutExtension(destinationPath));
        }
        catch (Exception exception)
        {
            _log.LogWarning($"[Saves] copy succeeded but the native save list could not refresh: {exception.Message}");
        }
    }

    private void ShowSourceChoices(IReadOnlyList<SaveImportEntry> entries)
    {
        if (entries.Count == 0)
        {
            _view.ShowChoices(this, "No saves found", "There are no importable Solo or Multiplayer saves yet.", CancelButton());
            return;
        }
        var choices = new List<DialogButton>(entries.Count);
        foreach (SaveImportEntry entry in entries)
        {
            SaveImportEntry selectedEntry = entry;
            choices.Add(new DialogButton(entry.Label, () => SelectSource(selectedEntry)));
        }
        _view.ShowList(this, "Import a save copy", "Choose a source save. Its original will stay in place.", choices, CancelButton());
    }

    private void ShowDestinationChoices()
    {
        SaveImportEntry source = _source ?? throw new InvalidOperationException("Select a source save before choosing a destination.");
        _view.ShowChoices(this, $"Copy {source.Name}", "Choose where the copy should be created.",
            new DialogButton("Solo", () => SelectDestination(SaveCollection.Solo)),
            new DialogButton("Multiplayer", () => SelectDestination(SaveCollection.Multiplayer)),
            new DialogButton("Back", ReopenSourceChoices));
    }

    private void ReopenSourceChoices()
    {
        _source = null;
        Open();
    }

    private void PromptForName(string initialName)
    {
        _view.ShowInput(this, "Name the copy", initialName, MaximumSaveNameLength, Copy, ShowDestinationChoices);
    }

    private void ShowLoadFailure(Exception exception)
    {
        _log.LogError($"[Saves] could not enumerate saves: {exception}");
        _view.ShowChoices(this, "Could not read saves", "No save files were changed. You can retry.",
            new DialogButton("Retry", Open), CancelButton());
    }

    private void ShowCopyFailure(Exception exception)
    {
        _log.LogWarning($"[Saves] copy failed: {exception.Message}");
        if (exception is SaveNameConflictException)
        {
            _view.ShowChoices(this, "Name already exists", "Choose a different name. Existing saves and the source were left unchanged.",
                new DialogButton("Choose another name", () => PromptForName(_source?.Name ?? string.Empty)),
                new DialogButton("Back", ShowDestinationChoices));
            return;
        }
        string message = exception is ArgumentException
            ? "Use a valid save name. No save files were changed."
            : "The copy failed. The source was kept unchanged; you can retry or choose another name.";
        _view.ShowChoices(this, "Could not copy save", message,
            new DialogButton("Retry", () => PromptForName(_source?.Name ?? string.Empty)),
            new DialogButton("Back", ShowDestinationChoices));
    }

    private DialogButton CancelButton() => new DialogButton("Cancel", Cancel);
}
