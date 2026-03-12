using System.Globalization;
using System.Reflection;
using System.Text;

namespace Trading.AI.PromptExecution;

public sealed class PromptInputConverter
{
    public PromptInputData Convert<TInput>(TInput input)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        var variables = input switch
        {
            IReadOnlyDictionary<string, string> readOnlyDictionary => CopyDictionary(readOnlyDictionary),
            IDictionary<string, string> dictionary => CopyDictionary(dictionary),
            _ => ConvertObject(input),
        };

        return new PromptInputData(
            variables,
            ResolvePromptDate(variables),
            ResolveRequestedAtUtc(variables));
    }

    private static IReadOnlyDictionary<string, string> ConvertObject<TInput>(TInput input)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal);
        var properties = input!.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public);

        foreach (var property in properties)
        {
            if (!property.CanRead || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            EnsureSupportedType(property.PropertyType, property.Name);

            var value = property.GetValue(input);
            if (value is null)
            {
                continue;
            }

            variables[ToUpperSnakeCase(property.Name)] = FormatValue(value);
        }

        return variables;
    }

    private static IReadOnlyDictionary<string, string> CopyDictionary(IEnumerable<KeyValuePair<string, string>> source)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in source)
        {
            variables[key] = value;
        }

        return variables;
    }

    private static DateOnly ResolvePromptDate(IReadOnlyDictionary<string, string> variables)
    {
        if (TryResolveDate(variables, "PROMPT_DATE", out var promptDate))
        {
            return promptDate;
        }

        if (TryResolveDate(variables, "TRADING_DATE", out var tradingDate))
        {
            return tradingDate;
        }

        return DateOnly.FromDateTime(ResolveRequestedAtUtc(variables).UtcDateTime);
    }

    private static bool TryResolveDate(IReadOnlyDictionary<string, string> variables, string key, out DateOnly value)
    {
        if (variables.TryGetValue(key, out var text)
            && DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    private static DateTimeOffset ResolveRequestedAtUtc(IReadOnlyDictionary<string, string> variables)
    {
        if (!variables.TryGetValue("REQUESTED_AT_UTC", out var text))
        {
            return DateTimeOffset.UtcNow;
        }

        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var requestedAtUtc))
        {
            return requestedAtUtc;
        }

        throw new InvalidOperationException($"Prompt input variable 'REQUESTED_AT_UTC' must be a valid UTC timestamp. Value: '{text}'.");
    }

    private static void EnsureSupportedType(Type propertyType, string propertyName)
    {
        var candidateType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        if (candidateType.IsEnum
            || candidateType == typeof(string)
            || candidateType == typeof(bool)
            || candidateType == typeof(byte)
            || candidateType == typeof(sbyte)
            || candidateType == typeof(short)
            || candidateType == typeof(ushort)
            || candidateType == typeof(int)
            || candidateType == typeof(uint)
            || candidateType == typeof(long)
            || candidateType == typeof(ulong)
            || candidateType == typeof(float)
            || candidateType == typeof(double)
            || candidateType == typeof(decimal)
            || candidateType == typeof(DateOnly)
            || candidateType == typeof(DateTime)
            || candidateType == typeof(DateTimeOffset)
            || candidateType == typeof(Guid))
        {
            return;
        }

        throw new InvalidOperationException(
            $"Prompt input property '{propertyName}' has unsupported type '{propertyType.Name}'. Format complex values into strings before executing the prompt.");
    }

    private static string FormatValue(object value)
        => value switch
        {
            string text => text,
            bool boolean => boolean ? "true" : "false",
            DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            DateTime dateTime => dateTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => value.ToString() ?? string.Empty,
        };

    private static string ToUpperSnakeCase(string name)
    {
        var builder = new StringBuilder(name.Length + 8);

        for (var index = 0; index < name.Length; index++)
        {
            var current = name[index];
            var hasPrevious = index > 0;
            var hasNext = index + 1 < name.Length;

            if (char.IsUpper(current) && hasPrevious && (char.IsLower(name[index - 1]) || (hasNext && char.IsLower(name[index + 1]))))
            {
                builder.Append('_');
            }

            builder.Append(char.ToUpperInvariant(current));
        }

        return builder.ToString();
    }
}
