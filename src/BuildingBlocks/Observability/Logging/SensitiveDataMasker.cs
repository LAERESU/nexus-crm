using System.Text.RegularExpressions;
using Serilog.Core;
using Serilog.Events;

namespace NexUS.BuildingBlocks.Observability;

/// <summary>
/// Menyamarkan nilai sensitif sebelum log ditulis (CONTEXT.md bagian 4 dan 8): properti bernama
/// password/token/secret dst., serta pola "Password=..." dan "Bearer ..." di dalam teks.
/// </summary>
public sealed partial class SensitiveDataMasker : ILogEventEnricher
{
    public const string Mask = "***";

    [GeneratedRegex("(password|passwd|pwd|secret|token|authorization|apikey|api_key|accesskey|connectionstring)", RegexOptions.IgnoreCase)]
    private static partial Regex SensitiveName();

    [GeneratedRegex(@"(?<key>password|pwd|secret[a-z]*)\s*=\s*[^;,\s]+", RegexOptions.IgnoreCase)]
    private static partial Regex KeyValueSecret();

    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9\-._~+/]+=*", RegexOptions.IgnoreCase)]
    private static partial Regex BearerToken();

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        foreach (var property in logEvent.Properties.ToList())
        {
            var masked = MaskProperty(property.Key, property.Value);
            if (!ReferenceEquals(masked, property.Value))
                logEvent.AddOrUpdateProperty(new LogEventProperty(property.Key, masked));
        }
    }

    internal static string MaskText(string text) =>
        BearerToken().Replace(KeyValueSecret().Replace(text, "${key}=" + Mask), "Bearer " + Mask);

    private static LogEventPropertyValue MaskProperty(string name, LogEventPropertyValue value) =>
        SensitiveName().IsMatch(name) ? new ScalarValue(Mask) : MaskValue(value);

    private static LogEventPropertyValue MaskValue(LogEventPropertyValue value)
    {
        switch (value)
        {
            case ScalarValue { Value: string text }:
                var maskedText = MaskText(text);
                return ReferenceEquals(maskedText, text) || maskedText == text ? value : new ScalarValue(maskedText);

            case StructureValue structure:
                return new StructureValue(
                    structure.Properties.Select(p => new LogEventProperty(p.Name, MaskProperty(p.Name, p.Value))),
                    structure.TypeTag);

            case DictionaryValue dictionary:
                return new DictionaryValue(dictionary.Elements.Select(e =>
                    new KeyValuePair<ScalarValue, LogEventPropertyValue>(
                        e.Key,
                        e.Key.Value is string key ? MaskProperty(key, e.Value) : MaskValue(e.Value))));

            case SequenceValue sequence:
                return new SequenceValue(sequence.Elements.Select(MaskValue));

            default:
                return value;
        }
    }
}
