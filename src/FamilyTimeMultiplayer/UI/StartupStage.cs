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
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.cullingMask = LayerMaskOf(book.gameObject);
        HideHudCanvases(book);
        DisableWorldEffects();
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
        _active = false;
        if (_camera != null)
        {
            _camera.clearFlags = _clearFlags;
            _camera.backgroundColor = _background;
            _camera.cullingMask = _cullingMask;
        }
        foreach (Canvas canvas in _hiddenCanvases)
        {
            if (canvas != null)
            {
                canvas.enabled = true;
            }
        }
        _hiddenCanvases.Clear();
        foreach (Behaviour effect in _disabledEffects)
        {
            if (effect != null)
            {
                effect.enabled = true;
            }
        }
        _disabledEffects.Clear();
    }

    private void HideHudCanvases(PauseMenuBook book)
    {
        foreach (Canvas canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
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
