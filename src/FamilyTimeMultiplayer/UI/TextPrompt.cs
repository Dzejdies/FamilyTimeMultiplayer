using System;

namespace FamilyTimeMultiplayer.UI;

public sealed class TextPrompt
{
    private readonly MultiplayerDialogView _view;
    private Action<string>? _onConfirm;

    public TextPrompt(MultiplayerDialogView view) => _view = view;

    public bool IsOpen => _onConfirm != null;

    public void Open(string title, string initialValue, int maxLength, Action<string> onConfirm)
    {
        _onConfirm = onConfirm ?? throw new ArgumentNullException(nameof(onConfirm));
        _view.ShowInput(this, title, initialValue, maxLength, ConfirmInput, Cancel);
    }

    public void Confirm(string value) => ConfirmValue(value);

    private void ConfirmInput(string value) => ConfirmValue(value);

    private void ConfirmValue(string value)
    {
        Action<string>? confirm = _onConfirm;
        _onConfirm = null;
        _view.Hide(this);
        if (!string.IsNullOrWhiteSpace(value)) confirm?.Invoke(value.Trim());
    }

    private void Cancel()
    {
        _onConfirm = null;
        _view.Hide(this);
    }
}
