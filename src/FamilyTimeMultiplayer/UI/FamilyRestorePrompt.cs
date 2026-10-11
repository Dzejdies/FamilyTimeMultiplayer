using FamilyTimeMultiplayer.Sessions;
using GAT;

namespace FamilyTimeMultiplayer.UI;

public sealed class FamilyRestorePrompt : System.IDisposable
{
    private const PlayerController.ReasonToShowCursor CursorReason = (PlayerController.ReasonToShowCursor)0x46544D50;
    private const PlayerController.ReasonToDisableAllControls ControlReason = (PlayerController.ReasonToDisableAllControls)0x46544D50;
    private readonly MultiplayerDialogView _view;
    private int? _shownPlayerId;
    private bool _active;

    public FamilyRestorePrompt(MultiplayerDialogView view) => _view = view;

    public void Dispose()
    {
        _view.Hide(this);
        PlayerController.showCursor.Remove(CursorReason);
        PlayerController.disableAllControls.Remove(ControlReason);
        _active = false;
        _shownPlayerId = null;
    }

    public void Update(ISession? session)
    {
        HostSession? host = session as HostSession;
        HostSession.FamilyRestoreRequest? request = host?.PendingFamilyRestores.Count > 0 ? host.PendingFamilyRestores[0] : null;
        if (request == null)
        {
            Hide();
            return;
        }
        SetControlsDisabled();
        if (_shownPlayerId != request.PlayerId || !_view.IsOwnedBy(this)) Show(host!, request);
    }

    private void Show(HostSession host, HostSession.FamilyRestoreRequest request)
    {
        _shownPlayerId = request.PlayerId;
        string text = $"{request.PlayerName} requests family '{request.FamilyName}'.\n\nRestore only for its original owner. Otherwise choose a new family or reject.\n\nPlayer identity: {request.Fingerprint}";
        _view.ShowChoices(this, "Restore a saved family", text,
            new DialogButton("Restore family", () => Decide(host, request, HostSession.FamilyRestoreChoice.Restore)),
            new DialogButton("New family", () => Decide(host, request, HostSession.FamilyRestoreChoice.NewFamily)),
            new DialogButton("Reject", () => Decide(host, request, HostSession.FamilyRestoreChoice.Reject)));
    }

    private void Decide(HostSession host, HostSession.FamilyRestoreRequest request, HostSession.FamilyRestoreChoice choice)
    {
        host.DecideFamilyRestore(request.PlayerId, choice);
        _shownPlayerId = null;
        _view.Hide(this);
    }

    private void SetControlsDisabled()
    {
        PlayerController.showCursor.Add(CursorReason);
        PlayerController.disableAllControls.Add(ControlReason);
        _active = true;
    }

    private void Hide()
    {
        _view.Hide(this);
        _shownPlayerId = null;
        if (!_active) return;
        PlayerController.showCursor.Remove(CursorReason);
        PlayerController.disableAllControls.Remove(ControlReason);
        _active = false;
    }
}
