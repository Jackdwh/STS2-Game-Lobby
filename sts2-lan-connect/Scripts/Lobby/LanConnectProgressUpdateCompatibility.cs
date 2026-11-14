using System.Reflection;
using System.Runtime.ExceptionServices;

namespace Sts2LanConnect.Scripts;

internal static class LanConnectProgressUpdateCompatibility
{
    // null means the legacy void API did not report a result. A false result is
    // "not applied", not necessarily an I/O failure (e.g. no local player in the save).
    internal static bool? Update(object manager, object run, bool victory)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(run);
        MethodInfo method = manager.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .SingleOrDefault(candidate => candidate.Name == "UpdateProgressWithRunData"
                && candidate.GetParameters() is { Length: 2 } parameters
                && parameters[0].ParameterType.IsInstanceOfType(run)
                && parameters[1].ParameterType == typeof(bool))
            ?? throw new MissingMethodException(manager.GetType().FullName, "UpdateProgressWithRunData");
        object? result;
        try
        {
            result = method.Invoke(manager, [run, victory]);
        }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
        if (method.ReturnType == typeof(void))
        {
            return null;
        }
        return result != null && LanConnectGameModeCompatibility.ReadMember(result, "success") is bool success
            ? success
            : throw new InvalidOperationException("Game progress update returned an unsupported result shape.");
    }
}
