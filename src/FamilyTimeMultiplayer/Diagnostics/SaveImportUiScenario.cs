using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using BepInEx.Logging;
using FamilyTimeMultiplayer.Core.Saves;
using FamilyTimeMultiplayer.Game;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FamilyTimeMultiplayer.Diagnostics;

internal sealed class SaveImportUiScenario
{
    private const double StageTimeoutSeconds = 60;
    private const string SourceSaveName = "test";
    private readonly ManualLogSource _log;
    private readonly SaveCollectionService _collections = SaveCollectionService.RequireCurrent();
    private readonly string _sourcePath;
    private readonly string _forwardName = CreateUniqueSaveName("ftmp_e2e_forward_");
    private readonly string _reverseName = CreateUniqueSaveName("ftmp_e2e_reverse_");
    private readonly string _retryName = CreateUniqueSaveName("ftmp_e2e_retry_");
    private readonly string _extension;
    private readonly string _sourceHash;
    private Stage _stage;
    private double _stageStartedAt;
    private bool _started;
    private Task<bool>? _copyVerification;

    public SaveImportUiScenario(ManualLogSource log)
    {
        _log = log;
        _sourcePath = Path.Combine(_collections.GetDirectory(SaveCollection.Solo), SourceSaveName + _collections.GetNativeSaveExtension());
        _extension = _collections.GetNativeSaveExtension();
        _sourceHash = ComputeHash(_sourcePath);
    }

    public void Tick(double now, MultiplayerController controller)
    {
        if (IsFinished()) return;
        if (!_started) Advance(now, Stage.WaitingForFirstSource);
        _started = true;
        if (now - _stageStartedAt > StageTimeoutSeconds) Fail($"Timed out during {_stage}.");
        TickCurrentStage(now, controller);
    }

    private void TickCurrentStage(double now, MultiplayerController controller)
    {
        switch (_stage)
        {
            case Stage.WaitingForFirstSource: SelectButton(now, $"Solo — {SourceSaveName}", Stage.WaitingForForwardDestination); break;
            case Stage.WaitingForForwardDestination: SelectButton(now, "Multiplayer", Stage.WaitingForForwardName); break;
            case Stage.WaitingForForwardName: EnterName(now, _forwardName, Stage.WaitingForForwardCopy); break;
            case Stage.WaitingForForwardCopy: VerifyCopyStarted(now, SaveCollection.Multiplayer, _forwardName, Stage.WaitingForForwardHash); break;
            case Stage.WaitingForForwardHash: FinishForwardCopy(now, controller); break;
            case Stage.WaitingForReverseSource: SelectButton(now, $"Multiplayer — {_forwardName}", Stage.WaitingForReverseDestination); break;
            case Stage.WaitingForReverseDestination: SelectButton(now, "Solo", Stage.WaitingForReverseName); break;
            case Stage.WaitingForReverseName: EnterName(now, _reverseName, Stage.WaitingForReverseCopy); break;
            case Stage.WaitingForReverseCopy: VerifyCopyStarted(now, SaveCollection.Solo, _reverseName, Stage.WaitingForReverseHash); break;
            case Stage.WaitingForReverseHash: FinishReverseCopy(now, controller); break;
            case Stage.WaitingForConflictSource: SelectButton(now, $"Solo — {SourceSaveName}", Stage.WaitingForConflictDestination); break;
            case Stage.WaitingForConflictDestination: SelectButton(now, "Multiplayer", Stage.WaitingForConflictName); break;
            case Stage.WaitingForConflictName: EnterName(now, _forwardName, Stage.WaitingForConflictMessage); break;
            case Stage.WaitingForConflictMessage: FinishConflict(now); break;
            case Stage.WaitingForConflictHash: FinishConflictHash(now); break;
            case Stage.WaitingForRetryName: EnterName(now, _retryName, Stage.WaitingForRetryCopy); break;
            case Stage.WaitingForRetryCopy: VerifyCopyStarted(now, SaveCollection.Multiplayer, _retryName, Stage.WaitingForRetryHash); break;
            case Stage.WaitingForRetryHash: FinishRetryCopy(now); break;
            case Stage.WaitingForCloseButton: CloseSuccessDialog(now); break;
        }
    }

    private void SelectButton(double now, string buttonName, Stage nextStage)
    {
        Button? button = FindDialogButton(buttonName);
        if (button == null) return;
        button.onClick.Invoke();
        Advance(now, nextStage);
    }

    private void EnterName(double now, string name, Stage nextStage)
    {
        TMP_InputField? input = FindDialogCanvas()?.GetComponentInChildren<TMP_InputField>(includeInactive: true);
        if (input == null) return;
        input.text = name;
        if (!ClickButton("OK")) return;
        Advance(now, nextStage);
    }

    private void VerifyCopyStarted(double now, SaveCollection destination, string name, Stage nextStage)
    {
        if (ReadDialogTitle() != "Copy complete") return;
        string destinationPath = GetSavePath(destination, name);
        _copyVerification = Task.Run(() => string.Equals(ComputeHash(destinationPath), _sourceHash, StringComparison.Ordinal));
        Advance(now, nextStage);
    }

    private void FinishForwardCopy(double now, MultiplayerController controller)
    {
        if (!CompleteCopyVerification()) return;
        controller.OpenSaveImport();
        Advance(now, Stage.WaitingForReverseSource);
    }

    private void FinishReverseCopy(double now, MultiplayerController controller)
    {
        if (!CompleteCopyVerification()) return;
        controller.OpenSaveImport();
        Advance(now, Stage.WaitingForConflictSource);
    }

    private void FinishConflict(double now)
    {
        if (ReadDialogTitle() != "Name already exists") return;
        if (!File.Exists(GetSavePath(SaveCollection.Multiplayer, _forwardName)))
        {
            Fail("The existing multiplayer copy disappeared during a duplicate-name check.");
            return;
        }
        string existingPath = GetSavePath(SaveCollection.Multiplayer, _forwardName);
        _copyVerification = Task.Run(() => string.Equals(ComputeHash(existingPath), _sourceHash, StringComparison.Ordinal));
        Advance(now, Stage.WaitingForConflictHash);
    }

    private void FinishConflictHash(double now)
    {
        if (!CompleteCopyVerification()) return;
        if (Directory.GetFiles(_collections.GetDirectory(SaveCollection.Multiplayer), "*.ftmp-importing").Length != 0)
        {
            Fail("A temporary import file remained after the duplicate-name check.");
            return;
        }
        ClickButton("Choose another name");
        Advance(now, Stage.WaitingForRetryName);
    }

    private void FinishRetryCopy(double now)
    {
        if (!CompleteCopyVerification()) return;
        CleanupCopies();
        _log.LogInfo("[Scenario] save import copied Solo to Multiplayer and back, preserved bytes, rejected duplicate names, and retried successfully");
        Advance(now, Stage.WaitingForCloseButton);
    }

    private void CloseSuccessDialog(double now)
    {
        if (!ClickButton("Done")) return;
        Advance(now, Stage.Complete);
    }

    private bool CompleteCopyVerification()
    {
        if (_copyVerification?.IsCompleted != true) return false;
        if (_copyVerification.IsFaulted || !_copyVerification.Result)
        {
            Fail("The imported save bytes do not match the original.");
            return false;
        }
        _copyVerification = null;
        return true;
    }

    private void Fail(string reason)
    {
        _log.LogError($"[Scenario] save import UI test failed: {reason}");
        CleanupCopies();
        _stage = Stage.Failed;
    }

    private void CleanupCopies()
    {
        DeleteIfPresent(GetSavePath(SaveCollection.Multiplayer, _forwardName));
        DeleteIfPresent(GetSavePath(SaveCollection.Solo, _reverseName));
        DeleteIfPresent(GetSavePath(SaveCollection.Multiplayer, _retryName));
    }

    private string GetSavePath(SaveCollection collection, string name) =>
        Path.Combine(_collections.GetDirectory(collection), name + _extension);

    private static string ComputeHash(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using SHA256 algorithm = SHA256.Create();
        return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", string.Empty);
    }

    private void DeleteIfPresent(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException exception)
        {
            _log.LogWarning($"[Scenario] could not remove test save '{path}': {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            _log.LogWarning($"[Scenario] could not remove test save '{path}': {exception.Message}");
        }
    }

    private static string CreateUniqueSaveName(string prefix) => prefix + Guid.NewGuid().ToString("N").Substring(0, 8);

    private static Canvas? FindDialogCanvas() =>
        UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(canvas => canvas.name == "FamilyTimeMultiplayerDialog");

    private static Button? FindDialogButton(string name) => FindDialogCanvas()?.GetComponentsInChildren<Button>(true)
        .FirstOrDefault(button => button.isActiveAndEnabled && button.name == name);

    private static bool ClickButton(string name)
    {
        Button? button = FindDialogButton(name);
        if (button == null) return false;
        button.onClick.Invoke();
        return true;
    }

    private static string? ReadDialogTitle() => FindDialogCanvas()?.transform.Find("Panel/Title")?.GetComponent<TMP_Text>()?.text;

    private bool IsFinished() => _stage is Stage.Complete or Stage.Failed;

    private void Advance(double now, Stage nextStage)
    {
        _stage = nextStage;
        _stageStartedAt = now;
    }

    private enum Stage
    {
        WaitingForFirstSource,
        WaitingForForwardDestination,
        WaitingForForwardName,
        WaitingForForwardCopy,
        WaitingForForwardHash,
        WaitingForReverseSource,
        WaitingForReverseDestination,
        WaitingForReverseName,
        WaitingForReverseCopy,
        WaitingForReverseHash,
        WaitingForConflictSource,
        WaitingForConflictDestination,
        WaitingForConflictName,
        WaitingForConflictMessage,
        WaitingForConflictHash,
        WaitingForRetryName,
        WaitingForRetryCopy,
        WaitingForRetryHash,
        WaitingForCloseButton,
        Complete,
        Failed
    }
}
