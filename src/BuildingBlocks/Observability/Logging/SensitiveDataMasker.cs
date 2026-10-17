using System.Text.RegularExpressions;
using Serilog.Core;
using Serilog.Events;

namespace NexUS.BuildingBlocks.Observability;

/// <summary>
/// Menyamarkan nilai sensitif sebelum log ditulis (CONTEXT.md bagian 4 dan 8): properti yang namanya
/// berakhir dengan password/token/secret dst., serta pola "password=...", "token=...", JSON "password":"...",
/// "Bearer ..." dan "Basic ..." di dalam teks. Teks exception tidak disentuh; jangan menyertakan
/// exception yang membawa kredensial.
/// </summary>
public sealed partial class SensitiveDataMasker : ILogEventEnricher
{
    public const string Mask = "***";

    // Dicocokkan di akhir nama, sehingga "AccessToken" disamarkan tetapi "CancellationToken" dan "AuthorizationPolicy" tidak.
    [GeneratedRegex(@"(password|passwd|pwd|secret|apikey|api_key|accesskey|access_key|connectionstring|authorization|(?<![a-z])token|(access|refresh|id|api|bearer|auth)_?token)$", RegexOptions.IgnoreCase)]
    private static partial Regex SensitiveName();

    [GeneratedRegex(@"(?<key>password|pwd|secret[a-z]*|token|apikey|api_key|access_?key)\s*=\s*[^;,\s]+", RegexOptions.IgnoreCase)]
    private static partial Regex KeyValueSecret();

    [GeneratedRegex(@"(?<key>""(?:password|pwd|secret[a-z]*|token|apikey|access_?token|refresh_?token)"")\s*:\s*""[^""]*""", RegexOptions.IgnoreCase)]
    private static partial Regex JsonSecret();

    // Token asli memuat angka atau tanda baca (JWT: titik; Basic: '='), sehingga kata biasa seperti "authentication" tidak ikut disamarkan.
    [GeneratedRegex(@"\b(?<scheme>Bearer|Basic)\s+(?:(?=[A-Za-z0-9\-._~+/]*[0-9.\-_+/])[A-Za-z0-9\-._~+/]{12,}=*|[A-Za-z0-9\-._~+/]{12,}={1,2})", RegexOptions.IgnoreCase)]
    private static partial Regex AuthScheme();

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        // Salin dulu: AddOrUpdateProperty mengubah koleksi yang sedang dienumerasi.
        var changes = new List<LogEventProperty>();
        foreach (var property in logEvent.Properties)
        {
            var masked = MaskProperty(property.Key, property.Value);
            if (!ReferenceEquals(masked, property.Value))
                changes.Add(new LogEventProperty(property.Key, masked));
        }

        foreach (var change in changes) logEvent.AddOrUpdateProperty(change);
    }

    internal static string MaskText(string text)
    {
        var result = KeyValueSecret().Replace(text, "${key}=" + Mask);
        result = JsonSecret().Replace(result, "${key}:\"" + Mask + "\"");
        return AuthScheme().Replace(result, "${scheme} " + Mask);
    }

    private static LogEventPropertyValue MaskProperty(string name, LogEventPropertyValue value) =>
        SensitiveName().IsMatch(name) ? new ScalarValue(Mask) : MaskValue(value);

    /// <summary>Mengembalikan instance yang sama bila tidak ada yang berubah (menghindari alokasi di jalur panas).</summary>
    private static LogEventPropertyValue MaskValue(LogEventPropertyValue value)
    {
        switch (value)
        {
            case ScalarValue { Value: string text }:
                var maskedText = MaskText(text);
                return maskedText == text ? value : new ScalarValue(maskedText);

            case StructureValue structure:
            {
                var properties = new LogEventProperty[structure.Properties.Count];
                var changed = false;
                for (var i = 0; i < properties.Length; i++)
                {
                    var original = structure.Properties[i];
                    var masked = MaskProperty(original.Name, original.Value);
                    changed |= !ReferenceEquals(masked, original.Value);
                    properties[i] = ReferenceEquals(masked, original.Value) ? original : new LogEventProperty(original.Name, masked);
                }

                return changed ? new StructureValue(properties, structure.TypeTag) : value;
            }

            case DictionaryValue dictionary:
            {
                var changed = false;
                var elements = new List<KeyValuePair<ScalarValue, LogEventPropertyValue>>(dictionary.Elements.Count);
                foreach (var (key, element) in dictionary.Elements)
                {
                    var masked = key.Value is string name ? MaskProperty(name, element) : MaskValue(element);
                    changed |= !ReferenceEquals(masked, element);
                    elements.Add(new(key, masked));
                }

                return changed ? new DictionaryValue(elements) : value;
            }

            case SequenceValue sequence:
            {
                var changed = false;
                var elements = new List<LogEventPropertyValue>(sequence.Elements.Count);
                foreach (var element in sequence.Elements)
                {
                    var masked = MaskValue(element);
                    changed |= !ReferenceEquals(masked, element);
                    elements.Add(masked);
                }

                return changed ? new SequenceValue(elements) : value;
            }

            default:
                return value;
        }
    }
}
