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
        new(ActionId, SchemaVersion, new[] { new EntityKind("agent"), new EntityKind("rigid") }, requiresTarget: false);

    public bool TryValidate(ReadOnlyMemory<byte> payload, out string error)
    {
        try { TargetIdentity(Decode(payload), 1); error = string.Empty; return true; }
        catch (InvalidDataException exception) { error = exception.Message; return false; }
        catch (IOException exception) { error = exception.Message; return false; }
        catch (ArgumentException exception) { error = exception.Message; return false; }
    }

    public bool Authorize(GenericActionContext<object> context, ReadOnlyMemory<byte> payload, out string error)
    {
        CommandIntentMessage intent = Decode(payload);
        bool hasEntityTarget = intent.TargetKind is CommandTargetKind.Agent or CommandTargetKind.Rigid;
        if (hasEntityTarget != context.TargetIdentity.HasValue ||
            context.TargetIdentity is NetworkEntityId identity && TargetIdentity(intent, identity.WorldGeneration) != identity)
        {
            error = "Native command target must match its versioned entity envelope.";
            return false;
        }
        error = _authorize(context.AuthenticatedPlayerId, intent) ?? string.Empty;
        return error.Length == 0;
    }

    public void Execute(GenericActionContext<object> context, ReadOnlyMemory<byte> payload) =>
        _execute(context.AuthenticatedPlayerId, Decode(payload));

    public static NetworkEntityId? TargetIdentity(CommandIntentMessage intent, long generation)
    {
        if (intent.IsCommandLock && intent.TargetKind != CommandTargetKind.Agent)
            throw new InvalidDataException("Command lock requires a versioned agent target.");
        string? kind = intent.TargetKind switch { CommandTargetKind.Agent => "agent", CommandTargetKind.Rigid => "rigid", _ => null };
        if (kind == null) return null;
        if (intent.RigidNetId < 0) throw new InvalidDataException("Native command entity identity is invalid.");
        return new NetworkEntityId(generation, new EntityKind(kind), (ulong)intent.RigidNetId);
    }

    public static CommandIntentMessage Decode(ReadOnlyMemory<byte> payload) =>
        MessageCodec.Decode(payload.ToArray()) as CommandIntentMessage
        ?? throw new InvalidDataException("Native action payload must contain a command intent.");
}
