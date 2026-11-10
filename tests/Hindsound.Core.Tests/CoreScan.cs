using System.Reflection;
using System.Reflection.Emit;

namespace Hindsound.Tests;

/// <summary>
/// Reads an assembly, its signatures and the IL of its methods, for what Hindsound.Core must not reach: the Windows
/// layer of the app, WASAPI, the registry, anything the runtime marks as Windows only, and the ways to load a native
/// library by hand. This file is compiled into both test projects: Hindsound.Core.Tests runs the scan on any system,
/// Hindsound.Tests adds the check against the types of the Windows layer.
/// </summary>
internal static class CoreScan
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(code => code.Value);

    /// <summary>Types whose every member loads or calls something native by a name or an address the code gives.</summary>
    private static readonly string[] ByHandTypes = ["System.Runtime.InteropServices.NativeLibrary"];

    /// <summary>Members that do the same, or that make an object of the COM world, from types that otherwise stay allowed.</summary>
    private static readonly (string Type, string Member)[] ByHandMembers =
    [
        ("System.Runtime.InteropServices.Marshal", "GetDelegateForFunctionPointer"),
        ("System.Type", "GetTypeFromCLSID"),
        ("System.Type", "GetTypeFromProgID"),
    ];

    /// <summary>Assemblies of the framework whose types are for Windows, whatever the types say of themselves.</summary>
    private static readonly string[] WindowsAssemblies =
    [
        "System.Security.Principal.Windows", "Microsoft.Win32.Registry", "Microsoft.Win32.SystemEvents", "System.Diagnostics.EventLog",
        "System.Drawing.Common", "System.Windows.Forms", "System.Management", "System.Diagnostics.PerformanceCounter",
    ];

    /// <summary>
    /// The members the runtime marks as Windows only that Hindsound.Core may reach, each behind a check of the system. Each one is
    /// written down with its reason: a new one fails the scan until it is looked at.
    /// </summary>
    private static readonly (string Type, string Member, string Reason)[] AllowedWindowsOnly =
    [
        // The apartment of a work thread that shows a Windows shell dialog; the call is behind OperatingSystem.IsWindows().
        ("System.Threading.Thread", "SetApartmentState", "the shell dialogs of the work thread"),
    ];

    /// <summary>Every type the declaration and the method bodies of <paramref name="type"/> name.</summary>
    public static IEnumerable<(string Where, Type Reached)> Reached(Type type)
    {
        if (type.BaseType is { } baseType)
        {
            yield return ("its base", baseType);
        }

        foreach (var face in type.GetInterfaces())
        {
            yield return ("an interface", face);
        }

        foreach (var field in type.GetFields(All))
        {
            yield return ($"field {field.Name}", field.FieldType);
        }

        foreach (var method in Methods(type))
        {
            if (method is MethodInfo info)
            {
                yield return ($"{method.Name} returns", info.ReturnType);
            }

            foreach (var parameter in method.GetParameters())
            {
                yield return ($"{method.Name} takes", parameter.ParameterType);
            }

            if (method.GetMethodBody() is not { } body)
            {
                continue;
            }

            foreach (var local in body.LocalVariables)
            {
                yield return ($"{method.Name} holds", local.LocalType);
            }

            foreach (var step in Walk(method, body.GetILAsByteArray() ?? []))
            {
                if (step.Member is { } member)
                {
                    foreach (var reached in TypesOf(member))
                    {
                        yield return ($"{method.Name} body", reached);
                    }
                }
            }
        }
    }

    /// <summary>Why the members and the calls in the method bodies of <paramref name="type"/> are not for any system, or nothing.</summary>
    public static IEnumerable<(string Where, string Why)> ForbiddenInBodies(Type type)
    {
        foreach (var method in Methods(type))
        {
            if (method.GetMethodBody() is not { } body)
            {
                continue;
            }

            foreach (var step in Walk(method, body.GetILAsByteArray() ?? []))
            {
                if (step.IsCalli)
                {
                    yield return ($"{method.Name} body", "a call through a function pointer (calli)");
                }
                else if (step.Member is { } member && ForbiddenMember(member) is { } why)
                {
                    yield return ($"{method.Name} body", why);
                }
            }
        }
    }

    /// <summary>Why this type may not be named by code meant for any system, or null.</summary>
    public static string? ForbiddenType(Type type)
    {
        while (type.HasElementType)
        {
            type = type.GetElementType()!;
        }

        if (type.IsGenericType && !type.IsGenericTypeDefinition)
        {
            foreach (var argument in type.GetGenericArguments())
            {
                if (ForbiddenType(argument) is { } inner)
                {
                    return inner;
                }
            }

            type = type.GetGenericTypeDefinition();
        }

        if (type.IsGenericParameter)
        {
            return null;
        }

        var name = type.FullName ?? type.Name;
        if (type.Assembly.GetName().Name == "NAudio.Wasapi")
        {
            return $"{name} (NAudio.Wasapi)";
        }

        if (type.Namespace is { } space && space.StartsWith("Microsoft.Win32", StringComparison.Ordinal) && space != "Microsoft.Win32.SafeHandles")
        {
            return $"{name} (Microsoft.Win32)";
        }

        if (ByHandTypes.Contains(name))
        {
            return $"{name} (loads a native library by hand)";
        }

        if (MarkedWindowsOnly(type) is { } marked)
        {
            return $"{name} ({marked})";
        }

        return null;
    }

    /// <summary>Why this member may not be called by code meant for any system, or null.</summary>
    public static string? ForbiddenMember(MemberInfo member)
    {
        var owner = member.DeclaringType;
        var ownerName = owner?.FullName ?? string.Empty;
        if (ByHandTypes.Contains(ownerName) || ByHandMembers.Contains((ownerName, member.Name)))
        {
            return $"{ownerName}.{member.Name} (loads or makes something native by hand)";
        }

        if (AllowedWindowsOnly.Any(allowed => allowed.Type == ownerName && allowed.Member == member.Name))
        {
            return null;
        }

        if (MarkedWindowsOnly(member) is { } marked)
        {
            return $"{ownerName}.{member.Name} ({marked})";
        }

        return null;
    }

    public static IEnumerable<MethodBase> Methods(Type type) =>
        type.GetMethods(All).Cast<MethodBase>().Concat(type.GetConstructors(All));

    public static Type Outermost(Type type)
    {
        while (type.DeclaringType is { } outer)
        {
            type = outer;
        }

        return type;
    }

    /// <summary>The Windows platform the runtime's own attributes put on the member, its type, or its assembly, or null.</summary>
    private static string? MarkedWindowsOnly(MemberInfo member)
    {
        // The nearest level that names platforms decides: an assembly that lists linux, macos and windows is not Windows only.
        for (MemberInfo? at = member; at is not null; at = at.DeclaringType)
        {
            if (Platforms(at.GetCustomAttributesData()) is { Count: > 0 } platforms)
            {
                return platforms.All(IsWindowsName) ? $"Windows only, marked on {at.Name}" : null;
            }
        }

        // Not the assembly: the one that runs on Windows is built for Windows and says so of itself, whatever it holds. The
        // assemblies that only make sense on Windows are named in WindowsAssemblies.
        var assembly = (member as Type ?? member.DeclaringType)?.Assembly.GetName().Name;
        return assembly is not null && WindowsAssemblies.Contains(assembly) ? $"Windows only, an assembly of {assembly}" : null;
    }

    private static bool IsWindowsName(string platform) => platform.StartsWith("windows", StringComparison.OrdinalIgnoreCase);

    private static List<string> Platforms(IEnumerable<CustomAttributeData> attributes) =>
        attributes
            .Where(data => data.AttributeType.FullName == "System.Runtime.Versioning.SupportedOSPlatformAttribute" && data.ConstructorArguments.Count > 0)
            .Select(data => data.ConstructorArguments[0].Value as string ?? string.Empty)
            .ToList();

    private static IEnumerable<Type> TypesOf(MemberInfo member)
    {
        switch (member)
        {
            case Type type:
                yield return type;
                break;
            case FieldInfo field:
                yield return field.DeclaringType!;
                yield return field.FieldType;
                break;
            case MethodBase called:
                yield return called.DeclaringType!;
                if (called is MethodInfo { IsGenericMethod: true } generic)
                {
                    foreach (var argument in generic.GetGenericArguments())
                    {
                        yield return argument;
                    }
                }

                break;
        }
    }

    /// <summary>Every type, field and method the IL of <paramref name="method"/> names, in the order it names them.</summary>
    public static IEnumerable<MemberInfo> MembersInBody(MethodBase method) =>
        Walk(method, method.GetMethodBody()?.GetILAsByteArray() ?? []).Select(step => step.Member).OfType<MemberInfo>();

    /// <summary>The member each instruction names, and the instructions that call through an address.</summary>
    private static IEnumerable<(MemberInfo? Member, bool IsCalli)> Walk(MethodBase method, byte[] il)
    {
        var typeArguments = method.DeclaringType is { IsGenericType: true } owner ? owner.GetGenericArguments() : null;
        var methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;
        var at = 0;
        while (at < il.Length)
        {
            short value = il[at++];
            if (value == 0xFE)
            {
                value = (short)(0xFE00 | il[at++]);
            }

            var code = OpCodesByValue[value];
            var size = code.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, at),
                _ => 4,
            };

            if (code.OperandType is OperandType.InlineField or OperandType.InlineMethod or OperandType.InlineType or OperandType.InlineTok)
            {
                yield return (method.Module.ResolveMember(BitConverter.ToInt32(il, at), typeArguments, methodArguments), false);
            }
            else if (code.OperandType == OperandType.InlineSig)
            {
                yield return (null, true);
            }

            at += size;
        }
    }
}
