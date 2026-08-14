using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace IronMeridian.Data
{
    /// <summary>How a tunable value is edited.</summary>
    public enum TunableKind
    {
        /// <summary>float / double — free text, parsed invariantly.</summary>
        Number,
        /// <summary>int — free text, parsed invariantly.</summary>
        Integer,
        /// <summary>bool — a two-state cycle button.</summary>
        Flag,
        /// <summary>string — free text.</summary>
        Text,
        /// <summary>enum — a cycle button stepping the declared values.</summary>
        Choice
    }

    /// <summary>
    /// One editable value on a data record, wrapped so a screen can read and
    /// write it without knowing what record it belongs to.
    ///
    /// **Why reflection.** The game's data tables are six different shapes —
    /// <see cref="UnitDefinition"/> plus five weapon catalogues, ~40 fields each
    /// — and the alternative to reflecting over them is six hand-written editor
    /// panels that go stale the moment somebody adds a field to a catalogue.
    /// The lab exists to show what the data *is*, so it has to be derived from
    /// the data rather than transcribed from it. This runs when a row is
    /// selected in a development screen, never in a combat tick.
    /// </summary>
    public sealed class TunableField
    {
        public readonly FieldInfo Info;
        public readonly string Label;
        public readonly TunableKind Kind;
        /// <summary>Declared values, for <see cref="TunableKind.Choice"/>.</summary>
        public readonly string[] Choices;

        public string Name => Info.Name;

        TunableField(FieldInfo info, TunableKind kind, string[] choices)
        {
            Info = info;
            Kind = kind;
            Choices = choices;
            Label = Prettify(info.Name);
        }

        /// <summary>The field's current value as an invariant, round-trippable string.</summary>
        public string Read(object target)
        {
            if (target == null) return "";
            object v = Info.GetValue(target);
            if (v == null) return "";
            return v switch
            {
                float f => f.ToString("0.#####", CultureInfo.InvariantCulture),
                double d => d.ToString("0.#####", CultureInfo.InvariantCulture),
                int i => i.ToString(CultureInfo.InvariantCulture),
                bool b => b ? "true" : "false",
                _ => v.ToString()
            };
        }

        /// <summary>
        /// Parses and writes a value. Returns false — leaving the field
        /// untouched — when the text does not parse: half-typed input is not an
        /// instruction to zero a stat.
        /// </summary>
        public bool Write(object target, string raw)
        {
            if (target == null) return false;
            raw = raw?.Trim() ?? "";

            switch (Kind)
            {
                case TunableKind.Number:
                    if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                        return false;
                    if (Info.FieldType == typeof(float)) Info.SetValue(target, (float)d);
                    else Info.SetValue(target, d);
                    return true;

                case TunableKind.Integer:
                    if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i))
                        return false;
                    Info.SetValue(target, i);
                    return true;

                case TunableKind.Flag:
                    if (bool.TryParse(raw, out bool b)) { Info.SetValue(target, b); return true; }
                    // "1"/"0" and "yes"/"no" round-trip too — the cycle button
                    // writes canonical text, but a hand-edited file may not.
                    if (raw == "1" || raw.Equals("yes", StringComparison.OrdinalIgnoreCase))
                    { Info.SetValue(target, true); return true; }
                    if (raw == "0" || raw.Equals("no", StringComparison.OrdinalIgnoreCase))
                    { Info.SetValue(target, false); return true; }
                    return false;

                case TunableKind.Choice:
                    // Matched against the declared names rather than through
                    // Enum.TryParse(Type, …), whose non-generic overload is not
                    // in every .NET profile this project can be built against.
                    foreach (string name in Choices)
                        if (string.Equals(name, raw, StringComparison.OrdinalIgnoreCase))
                        {
                            Info.SetValue(target, Enum.Parse(Info.FieldType, name));
                            return true;
                        }
                    return false;

                default:
                    Info.SetValue(target, raw);
                    return true;
            }
        }

        /// <summary>
        /// The next declared value, for the cycle buttons flags and enums are
        /// edited with. A dropdown would be the obvious control and is the wrong
        /// one here: these rows live inside a scroll view, and uGUI's Dropdown
        /// opens a template that has to be clipped by, and scroll with, its
        /// viewport — a cycle button has neither problem and needs one click.
        /// </summary>
        public string Cycle(object target, int step)
        {
            if (Kind == TunableKind.Flag)
                return Read(target) == "true" ? "false" : "true";

            if (Kind != TunableKind.Choice || Choices == null || Choices.Length == 0)
                return Read(target);

            int index = Array.IndexOf(Choices, Read(target));
            if (index < 0) index = 0;
            index = ((index + step) % Choices.Length + Choices.Length) % Choices.Length;
            return Choices[index];
        }

        /// <summary>"weaponRangeKm" -> "Weapon range km".</summary>
        static string Prettify(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            var sb = new System.Text.StringBuilder(name.Length + 8);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1])) sb.Append(' ');
                sb.Append(i == 0 ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        // ------------------------------------------------------- discovery

        static readonly Dictionary<Type, TunableField[]> _cache = new Dictionary<Type, TunableField[]>();

        /// <summary>
        /// Every editable field on a record type, in declaration order.
        ///
        /// Colours, arrays and nested objects are skipped rather than shown
        /// read-only: a row that cannot be edited in an editor is a row that
        /// only costs the reader time, and the catalogue source is a better
        /// place to read those than a table.
        /// </summary>
        public static IReadOnlyList<TunableField> Of(Type type)
        {
            if (type == null) return Array.Empty<TunableField>();
            if (_cache.TryGetValue(type, out var cached)) return cached;

            var fields = new List<(int token, TunableField field)>();
            foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (f.IsNotSerialized) continue;
                if (!Classify(f.FieldType, out TunableKind kind)) continue;

                string[] choices = kind == TunableKind.Choice ? Enum.GetNames(f.FieldType) : null;
                fields.Add((f.MetadataToken, new TunableField(f, kind, choices)));
            }

            // Declaration order is what makes the panel read like the catalogue
            // it mirrors. GetFields does not promise it; the metadata token does.
            fields.Sort((a, b) => a.token.CompareTo(b.token));

            var result = new TunableField[fields.Count];
            for (int i = 0; i < fields.Count; i++) result[i] = fields[i].field;
            _cache[type] = result;
            return result;
        }

        public static IReadOnlyList<TunableField> Of(object record) =>
            record == null ? Array.Empty<TunableField>() : Of(record.GetType());

        public static TunableField Find(object record, string name)
        {
            foreach (var f in Of(record)) if (f.Name == name) return f;
            return null;
        }

        static bool Classify(Type t, out TunableKind kind)
        {
            if (t == typeof(float) || t == typeof(double)) { kind = TunableKind.Number; return true; }
            if (t == typeof(int)) { kind = TunableKind.Integer; return true; }
            if (t == typeof(bool)) { kind = TunableKind.Flag; return true; }
            if (t == typeof(string)) { kind = TunableKind.Text; return true; }
            if (t.IsEnum) { kind = TunableKind.Choice; return true; }
            kind = TunableKind.Text;
            return false;
        }
    }
}
