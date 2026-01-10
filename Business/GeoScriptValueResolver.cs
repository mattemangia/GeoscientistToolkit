// GeoscientistToolkit/Business/GeoScript/GeoScriptValueResolver.cs

using System.Collections;
using System.Globalization;
using System.Reflection;
using GeoscientistToolkit.Data;

namespace GeoscientistToolkit.Business.GeoScript;

public static class GeoScriptValueResolver
{
    public static object Resolve(string reference, GeoScriptContext context)
    {
        if (string.IsNullOrWhiteSpace(reference))
            throw new ArgumentException("Variable reference cannot be empty.");

        var trimmed = reference.Trim();
        if (trimmed.Contains(' '))
            throw new NotSupportedException($"Command '{trimmed}' not recognized.");

        var segments = trimmed.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            throw new ArgumentException("Variable reference cannot be empty.");

        object current;
        var startIndex = 0;

        if (context.AvailableDatasets != null &&
            context.AvailableDatasets.TryGetValue(segments[0], out var dataset))
        {
            current = dataset;
            startIndex = 1;
        }
        else
        {
            current = context.InputDataset ?? throw new InvalidOperationException("No input dataset provided.");
        }

        if (startIndex >= segments.Length)
            return current;

        for (var i = startIndex; i < segments.Length; i++)
        {
            current = ResolveSegment(current, segments[i]);
        }

        return current;
    }

    private static object ResolveSegment(object current, string segment)
    {
        if (current == null)
            throw new InvalidOperationException($"Cannot resolve '{segment}' on a null value.");

        if (current is IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
            {
                if (entry.Key is string key && key.Equals(segment, StringComparison.OrdinalIgnoreCase))
                    return entry.Value;
            }
        }

        if (current is IEnumerable enumerable && current is not string)
        {
            if (int.TryParse(segment, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
                return GetEnumerableElement(enumerable, index);

            var match = FindEnumerableElementByName(enumerable, segment);
            if (match != null)
                return match;
        }

        var type = current.GetType();
        var property = type.GetProperty(segment,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (property != null)
            return property.GetValue(current);

        var field = type.GetField(segment,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (field != null)
            return field.GetValue(current);

        if (current is Dataset dataset && dataset.Metadata != null &&
            dataset.Metadata.TryGetValue(segment, out var metadataValue))
            return metadataValue;

        throw new ArgumentException($"Property '{segment}' was not found on '{type.Name}'.");
    }

    private static object GetEnumerableElement(IEnumerable enumerable, int index)
    {
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(index), "Index must be non-negative.");

        var i = 0;
        foreach (var item in enumerable)
        {
            if (i == index)
                return item;
            i++;
        }

        throw new ArgumentOutOfRangeException(nameof(index), $"Index {index} is out of range.");
    }

    private static object FindEnumerableElementByName(IEnumerable enumerable, string name)
    {
        foreach (var item in enumerable)
        {
            if (item == null)
                continue;

            var type = item.GetType();
            var nameProperty = type.GetProperty("Name",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (nameProperty != null)
            {
                var value = nameProperty.GetValue(item)?.ToString();
                if (!string.IsNullOrWhiteSpace(value) &&
                    value.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return item;
            }

            var idProperty = type.GetProperty("Id",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (idProperty != null)
            {
                var value = idProperty.GetValue(item)?.ToString();
                if (!string.IsNullOrWhiteSpace(value) &&
                    value.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return item;
            }
        }

        return null;
    }
}
