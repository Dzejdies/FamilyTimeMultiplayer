using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace FamilyTimeMultiplayer.UI;

public sealed class MultiplayerDialogView : MonoBehaviour
{
    private const float DialogWidth = 620f;
    private const float DialogHeight = 260f;
    private const float ListDialogHeight = 520f;
    private const float ChoiceListHeight = 250f;
    private const float ChoiceButtonHeight = 38f;
    private const float LayoutSpacing = 12f;
    private const float BodyHeight = 100f;
    private const float TitleHeight = 36f;
    private const float InputHeight = 42f;
    private const float ButtonRowHeight = 44f;
    private const float TitleFontSize = 26f;
    private const float BodyFontSize = 18f;
    private const float InputFontSize = 20f;
    private const float ButtonFontSize = 18f;
    private const float InputTextHorizontalInset = 20f;
    private const float InputTextVerticalInset = 6f;
    private static readonly Color PanelColor = new Color(0.08f, 0.1f, 0.12f, 0.98f);
    private static readonly Color InputColor = new Color(0.18f, 0.2f, 0.23f, 1f);
    private static readonly Color ButtonColor = new Color(0.2f, 0.34f, 0.42f, 1f);
    private GameObject _canvasObject = null!;
    private GameObject _panel = null!;
    private TMP_InputField _input = null!;
    private TextMeshProUGUI _title = null!;
    private TextMeshProUGUI _body = null!;
    private GameObject _choiceList = null!;
    private RectTransform _choiceContent = null!;
    private GameObject? _buttonRow;
    private object? _owner;

    public void ShowInput(object owner, string title, string value, int maxLength, Action<string> onConfirm, Action onCancel)
    {
        Claim(owner);
        BuildIfNeeded();
        _title.text = title;
        _body.text = string.Empty;
        _choiceList.SetActive(false);
        _input.gameObject.SetActive(true);
        _input.characterLimit = maxLength;
        _input.text = value;
        ReplaceButtons(new DialogButton("OK", () => onConfirm(_input.text)), new DialogButton("Cancel", onCancel));
        SetPanelHeight(DialogHeight);
        ShowPanel();
        EventSystem.current?.SetSelectedGameObject(_input.gameObject);
        _input.ActivateInputField();
    }

    public void ShowChoices(object owner, string title, string body, params DialogButton[] choices)
    {
        Claim(owner);
        BuildIfNeeded();
        _title.text = title;
        _body.text = body;
        _choiceList.SetActive(false);
        _input.gameObject.SetActive(false);
        ReplaceButtons(choices);
        SetPanelHeight(DialogHeight);
        ShowPanel();
    }

    public void ShowList(object owner, string title, string body, IReadOnlyList<DialogButton> choices, DialogButton cancel)
    {
        Claim(owner);
        BuildIfNeeded();
        _title.text = title;
        _body.text = body;
        _input.gameObject.SetActive(false);
        _choiceList.SetActive(true);
        ReplaceListChoices(choices);
        ReplaceButtons(cancel);
        SetPanelHeight(ListDialogHeight);
        ShowPanel();
    }

    public bool IsOwnedBy(object owner) => ReferenceEquals(_owner, owner);

    public void Hide(object owner)
    {
        if (!IsOwnedBy(owner)) return;
        _owner = null;
        if (_panel != null) _panel.SetActive(false);
    }

    private void Claim(object owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    private void BuildIfNeeded()
    {
        if (_panel != null) return;
        EnsureEventSystem();
        _canvasObject = new GameObject("FamilyTimeMultiplayerDialog", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = _canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = short.MaxValue;
        _canvasObject.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        _panel = CreatePanel(_canvasObject.transform);
        _title = CreateText("Title", _panel.transform, TitleFontSize, TextAlignmentOptions.Center);
        _body = CreateText("Body", _panel.transform, BodyFontSize, TextAlignmentOptions.TopLeft);
        _choiceList = CreateChoiceList(_panel.transform, out _choiceContent);
        _input = CreateInput(_panel.transform);
        _panel.SetActive(false);
    }

    private static GameObject CreatePanel(Transform canvas)
    {
        GameObject panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
        panel.transform.SetParent(canvas, false);
        RectTransform rect = (RectTransform)panel.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(DialogWidth, DialogHeight);
        panel.GetComponent<Image>().color = PanelColor;
        VerticalLayoutGroup layout = panel.GetComponent<VerticalLayoutGroup>();
        layout.spacing = LayoutSpacing;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;
        return panel;
    }

    private static TextMeshProUGUI CreateText(string name, Transform parent, float size, TextAlignmentOptions alignment)
    {
        GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        textObject.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)textObject.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;
        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        text.fontSize = size;
        text.color = Color.white;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.Normal;
        textObject.GetComponent<LayoutElement>().preferredHeight = name == "Body" ? BodyHeight : TitleHeight;
        return text;
    }

    private static TMP_InputField CreateInput(Transform parent)
    {
        GameObject fieldObject = new GameObject("Input", typeof(RectTransform), typeof(Image), typeof(TMP_InputField), typeof(LayoutElement));
        fieldObject.transform.SetParent(parent, false);
        fieldObject.GetComponent<Image>().color = InputColor;
        fieldObject.GetComponent<LayoutElement>().preferredHeight = InputHeight;
        TextMeshProUGUI value = CreateInputText(fieldObject.transform, "Value");
        TMP_InputField field = fieldObject.GetComponent<TMP_InputField>();
        field.textViewport = (RectTransform)value.transform.parent;
        field.textComponent = value;
        field.targetGraphic = fieldObject.GetComponent<Image>();
        field.lineType = TMP_InputField.LineType.SingleLine;
        return field;
    }

    private static TextMeshProUGUI CreateInputText(Transform parent, string name)
    {
        GameObject viewport = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
        viewport.transform.SetParent(parent, false);
        RectTransform viewportRect = (RectTransform)viewport.transform;
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.anchoredPosition = Vector2.zero;
        viewportRect.sizeDelta = new Vector2(-InputTextHorizontalInset, -InputTextVerticalInset);
        GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(viewport.transform, false);
        RectTransform textRect = (RectTransform)textObject.transform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.anchoredPosition = Vector2.zero;
        textRect.sizeDelta = Vector2.zero;
        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        text.fontSize = InputFontSize;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        return text;
    }

    private void ReplaceButtons(params DialogButton[] choices)
    {
        if (_buttonRow != null) Destroy(_buttonRow);
        GameObject row = new GameObject("Buttons", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        _buttonRow = row;
        row.transform.SetParent(_panel.transform, false);
        row.GetComponent<LayoutElement>().preferredHeight = ButtonRowHeight;
        HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = LayoutSpacing;
        layout.childControlHeight = layout.childControlWidth = true;
        layout.childForceExpandHeight = layout.childForceExpandWidth = true;
        foreach (DialogButton choice in choices) CreateButton(row.transform, choice);
    }

    private static GameObject CreateChoiceList(Transform parent, out RectTransform content)
    {
        GameObject list = new GameObject("ChoiceList", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(LayoutElement));
        list.transform.SetParent(parent, false);
        list.GetComponent<Image>().color = InputColor;
        list.GetComponent<LayoutElement>().preferredHeight = ChoiceListHeight;
        GameObject viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
        viewport.transform.SetParent(list.transform, false);
        RectTransform viewportRect = (RectTransform)viewport.transform;
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.sizeDelta = Vector2.zero;
        GameObject contentObject = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        contentObject.transform.SetParent(viewport.transform, false);
        content = (RectTransform)contentObject.transform;
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = Vector2.one;
        content.pivot = new Vector2(0.5f, 1f);
        content.sizeDelta = Vector2.zero;
        VerticalLayoutGroup layout = contentObject.GetComponent<VerticalLayoutGroup>();
        layout.spacing = LayoutSpacing;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;
        contentObject.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        ScrollRect scroll = list.GetComponent<ScrollRect>();
        scroll.viewport = viewportRect;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        return list;
    }

    private void ReplaceListChoices(IReadOnlyList<DialogButton> choices)
    {
        foreach (Transform child in _choiceContent) Destroy(child.gameObject);
        foreach (DialogButton choice in choices) CreateListButton(_choiceContent, choice);
        ScrollRect scroll = _choiceList.GetComponent<ScrollRect>();
        scroll.verticalNormalizedPosition = 1f;
    }

    private static GameObject CreateListButton(Transform parent, DialogButton choice)
    {
        GameObject buttonObject = CreateButton(parent, choice);
        LayoutElement layout = buttonObject.AddComponent<LayoutElement>();
        layout.preferredHeight = ChoiceButtonHeight;
        return buttonObject;
    }

    private static GameObject CreateButton(Transform parent, DialogButton choice)
    {
        GameObject buttonObject = new GameObject(choice.Label, typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);
        buttonObject.GetComponent<Image>().color = ButtonColor;
        buttonObject.GetComponent<Button>().onClick.AddListener(() => choice.OnClick());
        TextMeshProUGUI label = CreateText("Label", buttonObject.transform, ButtonFontSize, TextAlignmentOptions.Center);
        label.text = choice.Label;
        return buttonObject;
    }

    private void ShowPanel() => _panel.SetActive(true);

    private void SetPanelHeight(float height)
    {
        RectTransform panelRect = (RectTransform)_panel.transform;
        panelRect.sizeDelta = new Vector2(DialogWidth, height);
    }

    private void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        GameObject eventSystem = new GameObject("FamilyTimeMultiplayerEventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        eventSystem.transform.SetParent(transform, false);
    }
}

public readonly struct DialogButton
{
    public DialogButton(string label, Action onClick)
    {
        Label = label;
        OnClick = onClick;
    }

    public string Label { get; }
    public Action OnClick { get; }
}
