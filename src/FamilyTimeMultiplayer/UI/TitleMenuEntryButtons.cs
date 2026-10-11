using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace FamilyTimeMultiplayer.UI;

public sealed class TitleMenuEntryButtons : MonoBehaviour
{
    private const float ButtonWidth = 240f;
    private const float ButtonHeight = 68f;
    private const float ButtonGap = 24f;
    private const float BottomOffset = 110f;
    private const float LabelFontSize = 26f;
    private static readonly Color ButtonColor = new Color(0.12f, 0.16f, 0.2f, 0.92f);
    private Action _openSolo = null!;
    private Action _openMultiplayer = null!;
    private Rect _soloRect;
    private Rect _multiplayerRect;

    public void Initialize(Action openSolo, Action openMultiplayer)
    {
        _openSolo = openSolo;
        _openMultiplayer = openMultiplayer;
        CreateOverlay();
    }

    private void Update()
    {
        RefreshHitTargets();
        if (Mouse.current?.leftButton.wasPressedThisFrame == true)
        {
            HandleMouseClick(Mouse.current.position.ReadValue());
        }
        if (Keyboard.current?.enterKey.wasPressedThisFrame == true || Keyboard.current?.numpadEnterKey.wasPressedThisFrame == true)
        {
            _openSolo();
        }
    }

    private void HandleMouseClick(Vector2 position)
    {
        if (_soloRect.Contains(position)) _openSolo();
        else if (_multiplayerRect.Contains(position)) _openMultiplayer();
    }

    private void CreateOverlay()
    {
        GameObject canvasObject = new GameObject("FamilyTimeMultiplayerTitleChoices", typeof(RectTransform), typeof(Canvas));
        canvasObject.transform.SetParent(transform, false);
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue;
        CreateButton(canvasObject.transform, "Solo", -ButtonWidth - ButtonGap / 2f);
        CreateButton(canvasObject.transform, "Multiplayer", ButtonGap / 2f);
    }

    private void RefreshHitTargets()
    {
        float left = Screen.width / 2f - ButtonWidth - ButtonGap / 2f;
        float right = Screen.width / 2f + ButtonGap / 2f;
        _soloRect = new Rect(left, BottomOffset, ButtonWidth, ButtonHeight);
        _multiplayerRect = new Rect(right, BottomOffset, ButtonWidth, ButtonHeight);
    }

    private void CreateButton(Transform parent, string label, float horizontalOffset)
    {
        GameObject buttonObject = new GameObject(label, typeof(RectTransform), typeof(Image));
        buttonObject.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)buttonObject.transform;
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(horizontalOffset + ButtonWidth / 2f, BottomOffset);
        rect.sizeDelta = new Vector2(ButtonWidth, ButtonHeight);
        buttonObject.GetComponent<Image>().color = ButtonColor;
        CreateButtonLabel(buttonObject.transform, label);
    }

    private static void CreateButtonLabel(Transform parent, string label)
    {
        GameObject textObject = new GameObject("Label", typeof(RectTransform));
        textObject.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)textObject.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;
        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.text = label;
        text.alignment = TextAlignmentOptions.Center;
        text.fontSize = LabelFontSize;
        text.color = Color.white;
    }
}
