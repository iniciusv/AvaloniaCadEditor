using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using AvaloniaCadEditor.Domain.Features;
using AvaloniaCadEditor.Domain.ValueObjects;

namespace AvaloniaCadEditor.Application.Ai;

/// <summary>
/// Gera, via reflexão sobre o Domain, uma referência textual da API do "CadScript"
/// (o sub-dialeto de C# usado para descrever a cena) a ser injetada no system prompt da IA.
/// Como o texto é extraído do código real (construtores, propriedades e métodos públicos),
/// ele nunca fica desatualizado em relação às features/`ValueObjects` do projeto.
/// </summary>
public static class CadScriptApiReference
{
    private static readonly Type[] FeatureTypes =
    {
        typeof(BoxFeature),
        typeof(CylinderFeature),
        typeof(PrismFeature),
        typeof(BooleanFeature),
        typeof(FilletFeature)
    };

    private static readonly Type[] SupportTypes =
    {
        typeof(WorkPlane),
        typeof(LinkedWorkPlane),
        typeof(FaceReference),
        typeof(EdgeReference),
        typeof(Point3D),
        typeof(Vector3D)
    };

    private static readonly Type[] EnumTypes =
    {
        typeof(FaceType),
        typeof(EdgeType),
        typeof(BooleanOperationType),
        typeof(FilletMode),
        typeof(BoxCorner)
    };

    public static string Build()
    {
        var sb = new StringBuilder();

        sb.AppendLine("Propriedades comuns a TODAS as features (herdadas de FeatureBase):");
        foreach (var prop in typeof(FeatureBase)
                     .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(p => p.CanWrite && p.DeclaringType == typeof(FeatureBase)))
        {
            sb.AppendLine($"  {FormatProperty(prop)}");
        }
        sb.AppendLine();

        foreach (var type in FeatureTypes.Concat(SupportTypes))
        {
            AppendType(sb, type);
        }

        foreach (var type in EnumTypes)
        {
            AppendEnum(sb, type);
        }

        sb.AppendLine("Método global disponível no script: void AddFeature(IFeature feature) — registra a feature na cena.");

        return sb.ToString();
    }

    private static void AppendType(StringBuilder sb, Type type)
    {
        string header = type.Name;
        if (type.BaseType != null && type.BaseType != typeof(object) && type.BaseType != typeof(ValueType))
        {
            header += $" : {type.BaseType.Name}";
        }
        sb.AppendLine($"### {header}");

        foreach (var ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
        {
            sb.AppendLine($"  new {type.Name}({string.Join(", ", ctor.GetParameters().Select(FormatParameter))})");
        }

        foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                     .Where(m => !m.IsSpecialName))
        {
            sb.AppendLine($"  static {FormatType(m.ReturnType)} {type.Name}.{m.Name}({string.Join(", ", m.GetParameters().Select(FormatParameter))})");
        }

        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                     .Where(p => p.GetIndexParameters().Length == 0))
        {
            sb.AppendLine($"  {FormatProperty(p)}");
        }

        foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                     .Where(m => !m.IsSpecialName && m.DeclaringType == type))
        {
            sb.AppendLine($"  {FormatType(m.ReturnType)} {m.Name}({string.Join(", ", m.GetParameters().Select(FormatParameter))})");
        }

        sb.AppendLine();
    }

    private static void AppendEnum(StringBuilder sb, Type enumType)
    {
        sb.AppendLine($"enum {enumType.Name} {{ {string.Join(", ", Enum.GetNames(enumType))} }}");
    }

    private static string FormatProperty(PropertyInfo p)
    {
        string accessors = p.CanWrite ? "{ get; set; }" : "{ get; }";
        return $"{FormatType(p.PropertyType)} {p.Name} {accessors}";
    }

    private static string FormatParameter(ParameterInfo p)
    {
        string type = FormatType(p.ParameterType);
        bool nullableRef = !p.ParameterType.IsValueType && p.HasDefaultValue && p.DefaultValue is null && !type.EndsWith("?");
        if (nullableRef) type += "?";

        string prefix = p.GetCustomAttribute<ParamArrayAttribute>() != null ? "params " : "";
        string result = $"{prefix}{type} {p.Name}";
        if (p.HasDefaultValue)
        {
            result += $" = {FormatDefaultValue(p.DefaultValue)}";
        }
        return result;
    }

    private static string FormatDefaultValue(object? value)
    {
        return value switch
        {
            null => "null",
            bool b => b ? "true" : "false",
            string s => $"\"{s}\"",
            double d => d.ToString(CultureInfo.InvariantCulture),
            float f => f.ToString(CultureInfo.InvariantCulture),
            Enum e => $"{e.GetType().Name}.{e}",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "default"
        };
    }

    private static string FormatType(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying != null)
        {
            return FormatType(underlying) + "?";
        }

        if (type.IsGenericType)
        {
            string name = type.Name.Substring(0, type.Name.IndexOf('`'));
            string args = string.Join(", ", type.GetGenericArguments().Select(FormatType));
            return $"{name}<{args}>";
        }

        if (type.IsArray)
        {
            return FormatType(type.GetElementType()!) + "[]";
        }

        return type == typeof(double) ? "double"
            : type == typeof(int) ? "int"
            : type == typeof(string) ? "string"
            : type == typeof(bool) ? "bool"
            : type == typeof(void) ? "void"
            : type.Name;
    }
}
