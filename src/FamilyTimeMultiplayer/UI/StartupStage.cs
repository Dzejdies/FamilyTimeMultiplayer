using System;
using System.Collections.Generic;
using GAT;
using UnityEngine;

namespace FamilyTimeMultiplayer.UI;

public sealed class StartupStage
{
    private const string MultiplayerDialogCanvasName = "FamilyTimeMultiplayerDialog";
    private readonly List<Canvas> _hiddenCanvases = new List<Canvas>();
    private readonly List<Behaviour> _disabledEffects = new List<Behaviour>();
    private Camera? _camera;
    private CameraClearFlags _clearFlags;
    private Color _background;
    private int _cullingMask;
    private const CameraClearFlags AppliedClearFlags = CameraClearFlags.SolidColor;
    private Color _appliedBackground;
    private int _appliedCullingMask;
    private bool _active;

    public void Enter(PauseMenuBook book)
    {
        if (_active)
        {
            return;
        }
        Camera? camera = Camera.main;
        if (camera == null)
        {
            return;
        }
        _active = true;
        _camera = camera;
        _clearFlags = camera.clearFlags;
        _background = camera.backgroundColor;
        _cullingMask = camera.cullingMask;
        _appliedBackground = Color.black;
        _appliedCullingMask = LayerMaskOf(book.gameObject);
        try
        {
            camera.clearFlags = AppliedClearFlags;
            camera.backgroundColor = _appliedBackground;
            camera.cullingMask = _appliedCullingMask;
            HideHudCanvases(book);
            DisableWorldEffects();
        }
        catch
        {
            Exit();
            throw;
        }
    }

    private void DisableWorldEffects()
    {
        family.CloudManager? clouds = family.CloudManager.ActiveInstance;
        if (clouds != null && clouds.enabled)
        {
            clouds.enabled = false;
            _disabledEffects.Add(clouds);
        }
    }

    public void Exit()
    {
        if (!_active)
        {
            return;
        }
        Exception? restoreError = null;
        _active = false;
        try
        {
            if (_camera != null)
            {
                AttemptRestore(() => RestoreIfOwned(_camera.clearFlags, AppliedClearFlags, value => _camera.clearFlags = value, _clearFlags), ref restoreError);
                AttemptRestore(() => RestoreIfOwned(_camera.backgroundColor, _appliedBackground, value => _camera.backgroundColor = value, _background), ref restoreError);
                AttemptRestore(() => RestoreIfOwned(_camera.cullingMask, _appliedCullingMask, value => _camera.cullingMask = value, _cullingMask), ref restoreError);
            }
            RestoreCanvasesAndEffects(ref restoreError);
        }
        finally
        {
            _camera = null;
            _hiddenCanvases.Clear();
            _disabledEffects.Clear();
        }
        if (restoreError != null) throw restoreError;
    }

    private void RestoreCanvasesAndEffects(ref Exception? restoreError)
    {
        foreach (Canvas canvas in _hiddenCanvases)
        {
            if (canvas != null) AttemptRestore(() => RestoreIfOwned(canvas.enabled, false, value => canvas.enabled = value, true), ref restoreError);
        }
        foreach (Behaviour effect in _disabledEffects)
        {
            if (effect != null) AttemptRestore(() => RestoreIfOwned(effect.enabled, false, value => effect.enabled = value, true), ref restoreError);
        }
    }

    private static void RestoreIfOwned<T>(T currentValue, T appliedValue, Action<T> restore, T originalValue)
    {
        if (NativeOverrideRestoration.ShouldRestore(currentValue, appliedValue)) restore(originalValue);
    }

    private static void AttemptRestore(Action restore, ref Exception? firstError)
    {
        try
        {
            restore();
        }
        catch (Exception exception)
        {
            firstError ??= exception;
        }
    }

    private void HideHudCanvases(PauseMenuBook book)
    {
        foreach (Canvas canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!canvas.enabled || !canvas.isRootCanvas || canvas.name == MultiplayerDialogCanvasName ||
                canvas.renderMode == RenderMode.WorldSpace || canvas.transform.IsChildOf(book.transform))
            {
                continue;
            }
            canvas.enabled = false;
            _hiddenCanvases.Add(canvas);
        }
    }

    private static int LayerMaskOf(GameObject root)
    {
        int mask = 0;
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(includeInactive: true))
        {
            mask |= 1 << renderer.gameObject.layer;
        }
        return mask == 0 ? 1 << root.layer : mask;
    }
}
