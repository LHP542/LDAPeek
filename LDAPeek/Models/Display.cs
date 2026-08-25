using System.Globalization;

namespace LDAPeek.Models;

/// <summary>
/// Formatierung für die Oberfläche.
///
/// Bewusst hier statt per Konverter im XAML: ein fehlender Wert soll überall
/// dasselbe Zeichen zeigen, und ein Datum überall dasselbe Format. Über
/// <c>StringFormat</c>-Bindings verstreut sich beides und driftet mit der Zeit
/// auseinander.
/// </summary>
public static class Display
{
    /// <summary>Was bei einem leeren Wert steht.</summary>
    public const string Empty = "—";

    public const string DateTimeFormat = "dd.MM.yyyy HH:mm";
    public const string DateFormat = "dd.MM.yyyy";

    public static string Text(string? value) =>
        string.IsNullOrWhiteSpace(value) ? Empty : value;

    public static string Date(DateTimeOffset? value) =>
        value?.ToString(DateTimeFormat, CultureInfo.CurrentCulture) ?? Empty;

    public static string DateOnly(DateTimeOffset? value) =>
        value?.ToString(DateFormat, CultureInfo.CurrentCulture) ?? Empty;

    public static string Number(int value) => value.ToString(CultureInfo.CurrentCulture);

    /// <summary>
    /// Datum mit Abstand zu heute — „14.03.2026 08:12 (vor 12 Tagen)". Der
    /// absolute Wert allein beantwortet die eigentliche Frage nicht: bei
    /// „letzte Anmeldung" will man wissen, ob das Konto noch benutzt wird.
    /// </summary>
    public static string DateWithAge(DateTimeOffset? value)
    {
        if (value is not { } moment) return Empty;

        string absolute = moment.ToString(DateTimeFormat, CultureInfo.CurrentCulture);
        var age = DateTimeOffset.Now - moment;

        // Gerundet statt abgeschnitten: "AddDays(10)" ergibt eine Differenz von
        // 9,9999… Tagen, und ein (int)-Cast macht daraus "in 9 Tagen".
        static int Round(double value) => (int)Math.Round(Math.Abs(value), MidpointRounding.AwayFromZero);

        string relative = age switch
        {
            { TotalDays: >= 730 } => $"vor {Round(age.TotalDays / 365)} Jahren",
            { TotalDays: >= 60 } => $"vor {Round(age.TotalDays / 30)} Monaten",
            { TotalDays: >= 2 } => $"vor {Round(age.TotalDays)} Tagen",
            { TotalHours: >= 2 } => $"vor {Round(age.TotalHours)} Stunden",
            { TotalMinutes: >= 2 } => $"vor {Round(age.TotalMinutes)} Minuten",
            { Ticks: >= 0 } => "gerade eben",
            { TotalDays: <= -365 } => $"in {Round(age.TotalDays / 365)} Jahren",
            { TotalDays: <= -60 } => $"in {Round(age.TotalDays / 30)} Monaten",
            { TotalDays: <= -2 } => $"in {Round(age.TotalDays)} Tagen",
            _ => "demnächst",
        };

        return $"{absolute} ({relative})";
    }
}
