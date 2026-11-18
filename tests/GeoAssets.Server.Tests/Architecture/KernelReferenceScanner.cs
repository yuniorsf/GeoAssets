using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.Emit;

namespace GeoAssets.Server.Tests.Architecture;

/// <summary>
/// Reflection-based dependency scanner (XD01-352): finds, per top-level type, every referenced type a predicate
/// marks as forbidden — from signatures, attributes, locals and the method-body IL operands, so a call such as
/// <c>builder.Services.AddGeoAssetsPostgres()</c> inside <c>Program</c> counts even though no signature names
/// the provider type. Compiler-generated nested types (lambdas, closures, async state machines) are attributed
/// to their outermost declaring type.
/// </summary>
internal static class KernelReferenceScanner
{
    private const BindingFlags DeclaredMembers =
        BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    private static readonly OpCode?[] OneByteOpCodes = new OpCode?[0x100];
    private static readonly OpCode?[] TwoByteOpCodes = new OpCode?[0x100];

    static KernelReferenceScanner()
    {
        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is not OpCode opCode)
            {
                continue;
            }

            var value = (ushort)opCode.Value;
            if (opCode.Size == 1)
            {
                OneByteOpCodes[value] = opCode;
            }
            else
            {
                TwoByteOpCodes[value & 0xFF] = opCode;
            }
        }
    }

    public static IReadOnlyDictionary<string, IReadOnlySet<string>> FindForbiddenReferences(
        IEnumerable<Type> types, Func<Type, bool> isForbidden)
    {
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(isForbidden);

        var byOwner = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var type in types)
        {
            var found = new SortedSet<string>(StringComparer.Ordinal);
            var visit = new Action<Type?>(t => Collect(t, isForbidden, found));
            ScanType(type, visit);

            if (found.Count == 0)
            {
                continue;
            }

            var owner = OwnerOf(type);
            if (!byOwner.TryGetValue(owner, out var set))
            {
                set = new SortedSet<string>(StringComparer.Ordinal);
                byOwner[owner] = set;
            }

            set.UnionWith(found);
        }

        return byOwner.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlySet<string>)kv.Value,
            StringComparer.Ordinal);
    }

    public static string OwnerOf(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var outermost = type;
        while (outermost.DeclaringType is { } declaring)
        {
            outermost = declaring;
        }

        return outermost.FullName ?? outermost.Name;
    }

    private static void ScanType(Type type, Action<Type?> visit)
    {
        visit(type.BaseType);
        foreach (var implemented in type.GetInterfaces())
        {
            visit(implemented);
        }

        VisitAttributes(type.GetCustomAttributesData(), visit);

        foreach (var field in type.GetFields(DeclaredMembers))
        {
            visit(field.FieldType);
            VisitAttributes(field.GetCustomAttributesData(), visit);
        }

        foreach (var property in type.GetProperties(DeclaredMembers))
        {
            visit(property.PropertyType);
            VisitAttributes(property.GetCustomAttributesData(), visit);
        }

        foreach (var @event in type.GetEvents(DeclaredMembers))
        {
            visit(@event.EventHandlerType);
        }

        foreach (var constructor in type.GetConstructors(DeclaredMembers))
        {
            ScanMethod(constructor, visit);
        }

        foreach (var method in type.GetMethods(DeclaredMembers))
        {
            visit(method.ReturnType);
            ScanMethod(method, visit);
        }
    }

    private static void ScanMethod(MethodBase method, Action<Type?> visit)
    {
        VisitAttributes(method.GetCustomAttributesData(), visit);
        foreach (var parameter in method.GetParameters())
        {
            visit(parameter.ParameterType);
            VisitAttributes(parameter.GetCustomAttributesData(), visit);
        }

        var body = method.GetMethodBody();
        if (body is null)
        {
            return;
        }

        foreach (var local in body.LocalVariables)
        {
            visit(local.LocalType);
        }

        var il = body.GetILAsByteArray();
        if (il is not null)
        {
            ScanIl(method, il, visit);
        }
    }

    private static void ScanIl(MethodBase method, byte[] il, Action<Type?> visit)
    {
        var typeArguments = method.DeclaringType is { IsGenericType: true } declaring
            ? declaring.GetGenericArguments()
            : null;
        var methodArguments = method is { IsGenericMethod: true } ? method.GetGenericArguments() : null;

        var position = 0;
        while (position < il.Length)
        {
            OpCode? decoded = il[position] == 0xFE
                ? TwoByteOpCodes[il[++position]]
                : OneByteOpCodes[il[position]];
            position++;

            var opCode = decoded
                ?? throw new InvalidOperationException(
                    $"Unknown IL opcode at offset {position - 1} in {method.DeclaringType?.FullName}.{method.Name}.");

            switch (opCode.OperandType)
            {
                case OperandType.InlineField:
                case OperandType.InlineMethod:
                case OperandType.InlineTok:
                case OperandType.InlineType:
                    var token = BinaryPrimitives.ReadInt32LittleEndian(il.AsSpan(position, 4));
                    VisitMember(method.Module.ResolveMember(token, typeArguments, methodArguments), visit);
                    position += 4;
                    break;
                case OperandType.InlineSwitch:
                    var targets = BinaryPrimitives.ReadInt32LittleEndian(il.AsSpan(position, 4));
                    position += 4 + (targets * 4);
                    break;
                default:
                    position += OperandSize(opCode.OperandType);
                    break;
            }
        }
    }

    private static int OperandSize(OperandType operandType) => operandType switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        _ => 4,
    };

    private static void VisitMember(MemberInfo? member, Action<Type?> visit)
    {
        switch (member)
        {
            case Type type:
                visit(type);
                break;
            case FieldInfo field:
                visit(field.DeclaringType);
                visit(field.FieldType);
                break;
            case MethodBase called:
                visit(called.DeclaringType);
                if (called is MethodInfo methodInfo)
                {
                    visit(methodInfo.ReturnType);
                    if (methodInfo.IsGenericMethod)
                    {
                        foreach (var argument in methodInfo.GetGenericArguments())
                        {
                            visit(argument);
                        }
                    }
                }

                foreach (var parameter in called.GetParameters())
                {
                    visit(parameter.ParameterType);
                }

                break;
        }
    }

    private static void VisitAttributes(IEnumerable<CustomAttributeData> attributes, Action<Type?> visit)
    {
        foreach (var attribute in attributes)
        {
            visit(attribute.AttributeType);
            foreach (var argument in attribute.ConstructorArguments.Concat(attribute.NamedArguments.Select(n => n.TypedValue)))
            {
                visit(argument.Value as Type);
            }
        }
    }

    private static void Collect(Type? type, Func<Type, bool> isForbidden, SortedSet<string> found)
    {
        if (type is null || type.IsGenericParameter)
        {
            return;
        }

        if (type.HasElementType)
        {
            Collect(type.GetElementType(), isForbidden, found);
            return;
        }

        if (type.IsGenericType && !type.IsGenericTypeDefinition)
        {
            foreach (var argument in type.GetGenericArguments())
            {
                Collect(argument, isForbidden, found);
            }

            type = type.GetGenericTypeDefinition();
        }

        if (isForbidden(type))
        {
            found.Add(type.FullName ?? type.Name);
        }
    }
}
