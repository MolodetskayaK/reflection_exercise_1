using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;

public sealed class ReflectionCsvSerializer : IStringSerializer
{
    private readonly char _delimiter;
    private readonly char _arrayDelimiter;

    private static readonly ConcurrentDictionary<Type, TypeMap> Cache = new();

    public ReflectionCsvSerializer(char delimiter = ';', char arrayDelimiter = '|')
    {
        _delimiter = delimiter;
        _arrayDelimiter = arrayDelimiter;
    }

    public string Serialize(object obj)
    {
        if (obj is null) throw new ArgumentNullException(nameof(obj));

        var map = Cache.GetOrAdd(obj.GetType(), BuildMap);

        var sb = new StringBuilder(256);
        for (int i = 0; i < map.Members.Length; i++)
        {
            if (i > 0) sb.Append(_delimiter);

            var m = map.Members[i];
            var cell = ToCell(m.Getter(obj), m.Type);
            sb.Append(Escape(cell));
        }

        return sb.ToString();
    }

    public object Deserialize(string data, Type type)
    {
        if (data is null) throw new ArgumentNullException(nameof(data));
        if (type is null) throw new ArgumentNullException(nameof(type));

        var map = Cache.GetOrAdd(type, BuildMap);
        var cells = ParseCsvLine(data, _delimiter);

        if (cells.Count != map.Members.Length)
            throw new FormatException($"Columns mismatch: expected {map.Members.Length}, got {cells.Count}.");

        var obj = Activator.CreateInstance(type)
                  ?? throw new InvalidOperationException($"Cannot create instance: {type.FullName}");

        for (int i = 0; i < map.Members.Length; i++)
        {
            var m = map.Members[i];
            var value = FromCell(cells[i], m.Type);
            m.Setter(obj, value);
        }

        return obj;
    }

    public T Deserialize<T>(string data) where T : new() => (T)Deserialize(data, typeof(T));

    private static TypeMap BuildMap(Type t)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        var fields = t.GetFields(flags)
            .Where(f => !f.IsStatic && !f.IsInitOnly)
            .Select(MemberAccessor.FromField);

        var props = t.GetProperties(flags)
            .Where(p => p.GetIndexParameters().Length == 0 && p.CanRead && p.CanWrite)
            .Select(MemberAccessor.FromProperty);

        var members = fields.Concat(props)
            .OrderBy(m => m.Name, StringComparer.Ordinal)
            .ToArray();

        return new TypeMap(members);
    }

    private sealed record TypeMap(MemberAccessor[] Members);

    private sealed class MemberAccessor
    {
        public string Name { get; }
        public Type Type { get; }
        public Func<object, object?> Getter { get; }
        public Action<object, object?> Setter { get; }

        private MemberAccessor(string name, Type type, Func<object, object?> getter, Action<object, object?> setter)
        {
            Name = name;
            Type = type;
            Getter = getter;
            Setter = setter;
        }

        public static MemberAccessor FromField(FieldInfo f)
        {
            var obj = Expression.Parameter(typeof(object), "obj");
            var val = Expression.Parameter(typeof(object), "val");

            var typedObj = Expression.Convert(obj, f.DeclaringType!);
            var field = Expression.Field(typedObj, f);

            var getter = Expression.Lambda<Func<object, object?>>(
                Expression.Convert(field, typeof(object)), obj).Compile();

            var setter = Expression.Lambda<Action<object, object?>>(
                Expression.Assign(field, Expression.Convert(val, f.FieldType)), obj, val).Compile();

            return new MemberAccessor(f.Name, f.FieldType, getter, setter);
        }

        public static MemberAccessor FromProperty(PropertyInfo p)
        {
            var obj = Expression.Parameter(typeof(object), "obj");
            var val = Expression.Parameter(typeof(object), "val");

            var typedObj = Expression.Convert(obj, p.DeclaringType!);
            var prop = Expression.Property(typedObj, p);

            var getter = Expression.Lambda<Func<object, object?>>(
                Expression.Convert(prop, typeof(object)), obj).Compile();

            var setter = Expression.Lambda<Action<object, object?>>(
                Expression.Assign(prop, Expression.Convert(val, p.PropertyType)), obj, val).Compile();

            return new MemberAccessor(p.Name, p.PropertyType, getter, setter);
        }
    }

    private string ToCell(object? value, Type type)
    {
        if (value is null) return string.Empty;

        if (type.IsArray && value is Array arr)
        {
            var elemType = type.GetElementType()!;
            var parts = new string[arr.Length];
            for (int i = 0; i < arr.Length; i++)
                parts[i] = ScalarToString(arr.GetValue(i), elemType);

            return string.Join(_arrayDelimiter, parts);
        }

        return ScalarToString(value, type);
    }

    private object? FromCell(string cell, Type type)
    {
        if (string.IsNullOrEmpty(cell))
        {
            if (!type.IsValueType || Nullable.GetUnderlyingType(type) is not null) return null;
            return Activator.CreateInstance(type);
        }

        if (type.IsArray)
        {
            var elemType = type.GetElementType()!;
            var parts = cell.Split(_arrayDelimiter, StringSplitOptions.None);
            var arr = Array.CreateInstance(elemType, parts.Length);

            for (int i = 0; i < parts.Length; i++)
                arr.SetValue(ParseScalar(parts[i], elemType), i);

            return arr;
        }

        return ParseScalar(cell, type);
    }

    private static string ScalarToString(object? value, Type type)
    {
        if (value is null) return string.Empty;
        if (type == typeof(string)) return (string)value;
        if (type == typeof(bool)) return (bool)value ? "true" : "false";
        if (type == typeof(DateTime)) return ((DateTime)value).ToString("O", CultureInfo.InvariantCulture);
        if (type.IsEnum) return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;

        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static object? ParseScalar(string text, Type type)
    {
        var u = Nullable.GetUnderlyingType(type);
        if (u is not null)
        {
            if (string.IsNullOrEmpty(text)) return null;
            type = u;
        }

        if (type == typeof(string)) return text;
        if (type == typeof(int)) return int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (type == typeof(long)) return long.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
        if (type == typeof(double)) return double.Parse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture);
        if (type == typeof(float)) return float.Parse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture);
        if (type == typeof(decimal)) return decimal.Parse(text, NumberStyles.Number, CultureInfo.InvariantCulture);
        if (type == typeof(bool)) return string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) || bool.Parse(text);
        if (type == typeof(DateTime)) return DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        if (type.IsEnum) return Enum.Parse(type, text, ignoreCase: true);

        return Convert.ChangeType(text, type, CultureInfo.InvariantCulture);
    }

    private string Escape(string cell)
    {
        var mustQuote = cell.IndexOfAny(new[] { _delimiter, '"', '\r', '\n' }) >= 0;
        if (!mustQuote) return cell;
        return $"\"{cell.Replace("\"", "\"\"")}\"";
    }

    private static List<string> ParseCsvLine(string line, char delimiter)
    {
        var result = new List<string>(32);
        var sb = new StringBuilder(128);

        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else inQuotes = false;
                }
                else sb.Append(c);
            }
            else
            {
                if (c == '"') inQuotes = true;
                else if (c == delimiter)
                {
                    result.Add(sb.ToString());
                    sb.Clear();
                }
                else sb.Append(c);
            }
        }

        result.Add(sb.ToString());
        return result;
    }
}
