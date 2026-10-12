using System;
using System.IO;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Core.Replication;

namespace FamilyTimeMultiplayer.Core.Actions;

public sealed class NativeCommandActionAdapter : IGenericActionAdapter<object>
{
    public const string ActionId = "familytime.command";
    public const int SchemaVersion = 1;
    private readonly Action<int, CommandIntentMessage> _execute;
    private readonly Func<int, CommandIntentMessage, string?> _authorize;

    public NativeCommandActionAdapter(Action<int, CommandIntentMessage> execute,
        Func<int, CommandIntentMessage, string?> authorize)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _authorize = authorize ?? throw new ArgumentNullException(nameof(authorize));
    }

    public GenericActionDescriptor Descriptor { get; } =
        new(ActionId, SchemaVersion, Array.Empty<EntityKind>(), requiresTarget: false);

    public bool TryValidate(ReadOnlyMemory<byte> payload, out string error)
    {
        try { Decode(payload); error = string.Empty; return true; }
        catch (InvalidDataException exception) { error = exception.Message; return false; }
        catch (IOException exception) { error = exception.Message; return false; }
        catch (ArgumentException exception) { error = exception.Message; return false; }
    }

    public bool Authorize(GenericActionContext<object> context, ReadOnlyMemory<byte> payload, out string error)
    {
        error = _authorize(context.AuthenticatedPlayerId, Decode(payload)) ?? string.Empty;
        return error.Length == 0;
    }

    public void Execute(GenericActionContext<object> context, ReadOnlyMemory<byte> payload) =>
        _execute(context.AuthenticatedPlayerId, Decode(payload));

    public static CommandIntentMessage Decode(ReadOnlyMemory<byte> payload) =>
        MessageCodec.Decode(payload.ToArray()) as CommandIntentMessage
        ?? throw new InvalidDataException("Native action payload must contain a command intent.");
}
