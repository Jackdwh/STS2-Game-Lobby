using System.Reflection;
using System.Runtime.ExceptionServices;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectNetGameServiceCompatibility
{
    internal static void DisconnectClient(NetHostGameService host, ulong peerId, NetError reason, bool now = false)
    {
        // 0.113 adds an optional debugReason parameter, which still changes the CLR ABI.
        Type type = typeof(NetHostGameService);
        MethodInfo? method = type.GetMethod("DisconnectClient",
            [typeof(ulong), typeof(NetError), typeof(bool), typeof(string)]);
        object?[] args;
        if (method != null)
        {
            args = [peerId, reason, now, null];
        }
        else
        {
            method = type.GetMethod("DisconnectClient", [typeof(ulong), typeof(NetError), typeof(bool)])
                ?? throw new MissingMethodException(type.FullName, "DisconnectClient");
            args = [peerId, reason, now];
        }
        try
        {
            method.Invoke(host, args);
        }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
        }
    }
}
