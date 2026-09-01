using System.Runtime.Versioning;
using Avalonia.Interactivity;
using LDAPeek.Models;
using LDAPeek.Services;
using NLog;

namespace LDAPeek.Views;

/// <summary>
/// Wird statt des Hauptfensters gezeigt, wenn der angemeldete Benutzer nicht in
/// der geforderten Gruppe ist.
///
/// Bewusst mit „Erneut prüfen": Der häufigste Grund für ein unerwartetes Nein
/// ist eine Aufnahme in die Gruppe, die das Windows-Anmeldetoken noch nicht
/// kennt. Ohne diesen Knopf bliebe dem Nutzer nur Abmelden und Neuanmelden —
/// die Rückfrage im Verzeichnis erledigt es sofort.
/// </summary>
[SupportedOSPlatform("windows")]
public partial class AccessDeniedWindow : ChromeWindow
{
    private static readonly ILogger Log = LogManager.GetCurrentClassLogger();

    private readonly AccessGate? _gate;

    /// <summary>true, wenn die Wiederholung Zugang ergeben hat.</summary>
    public bool AccessGranted { get; private set; }

    /// <summary>Parameterloser Konstruktor für den XAML-Designer.</summary>
    public AccessDeniedWindow()
    {
        InitializeComponent();
    }

    internal AccessDeniedWindow(AccessGate gate, AccessCheckResult result) : this()
    {
        _gate = gate;
        Show(result);

        RetryButton.Click += OnRetry;
        CloseButton.Click += (_, _) => Close();

        // Die Rückfrage im Verzeichnis einmal von selbst stellen, statt sie
        // hinter dem Knopf zu verstecken: Der häufigste Grund für ein
        // unerwartetes Nein ist eine gerade erfolgte Aufnahme in die Gruppe,
        // die das Anmeldetoken noch nicht kennt. Wer davon nichts weiß, würde
        // sonst gar nicht auf die Idee kommen, „Erneut prüfen" zu drücken.
        //
        // Nur wenn das Token die Ursache war — bei einer unauflösbaren Gruppe
        // oder defekter Regel hilft keine Abfrage.
        if (result.Source == AccessCheckSource.LogonToken)
        {
            Opened += async (_, _) => await RunCheckAsync();
        }
    }

    private void Show(AccessCheckResult result)
    {
        LeadText.Text = result.Source == AccessCheckSource.Unresolved
            ? "Der Zugang konnte nicht bestätigt werden."
            : "Für LDAPeek ist eine Gruppenmitgliedschaft erforderlich, die dein Konto nicht hat.";

        AccountText.Text = result.Account;
        GroupText.Text = Display.Text(result.RequiredGroup);

        string? hint = result.Problem ?? _gate?.Policy.ContactHint;
        HintText.Text = hint ?? string.Empty;
        HintCard.IsVisible = !string.IsNullOrWhiteSpace(hint);
    }

    private async void OnRetry(object? sender, RoutedEventArgs e) => await RunCheckAsync();

    private async Task RunCheckAsync()
    {
        if (_gate is null) return;

        RetryButton.IsEnabled = false;
        CheckProgress.IsVisible = true;

        try
        {
            var result = await _gate.CheckAsync();
            if (result.Granted)
            {
                Log.Info("Zugang bei der Rückfrage im Verzeichnis bestätigt — LDAPeek startet.");
                AccessGranted = true;
                Close();
                return;
            }

            Show(result);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Zugangsprüfung fehlgeschlagen.");
            HintText.Text = "Die Prüfung ist fehlgeschlagen. Details stehen im Log.";
            HintCard.IsVisible = true;
        }
        finally
        {
            CheckProgress.IsVisible = false;
            RetryButton.IsEnabled = true;
        }
    }
}
