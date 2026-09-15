namespace VstManager.Ui.Services;

/// <summary>
/// The app-wide content zoom factor (1.0 = 100%).
///
/// A global static rather than view-model state because the zoom must reach every window, and
/// several of them (Welcome, Classify, MatchPicker, CloudConflict, Settings) do not share
/// MainViewModel as their DataContext. MainViewModel owns only persistence; this is the single
/// source of truth the windows actually read.
///
/// The WPF head carries an equivalent service with the same bounds, and both persist to the same
/// LibraryData.UiScale — so a zoom set in one head is the zoom the other opens at.
/// </summary>
public static class UiScale
{
    public const double Min = 0.8;
    public const double Max = 1.5;
    public const double Step = 0.1;

    private static double _factor = 1.0;

    /// <summary>Raised whenever <see cref="Factor"/> changes, so open windows re-scale live.</summary>
    public static event Action? Changed;

    /// <summary>The current zoom, clamped to [<see cref="Min"/>, <see cref="Max"/>].</summary>
    public static double Factor
    {
        get => _factor;
        set
        {
            var clamped = Math.Clamp(Math.Round(value, 2), Min, Max);

            // Rounded to 2dp above, so this only skips genuine no-ops — including the repeated
            // steps that pile up once the user holds Ctrl+ at either end.
            if (Math.Abs(clamped - _factor) < 0.001)
            {
                return;
            }

            _factor = clamped;
            Changed?.Invoke();
        }
    }

    /// <summary>Whether another step in this direction would change anything, for greying out UI.</summary>
    public static bool CanStep(double steps) =>
        Math.Abs(Math.Clamp(Math.Round(_factor + steps * Step, 2), Min, Max) - _factor) >= 0.001;

    /// <summary>Nudges the zoom by one step, for the Ctrl +/- shortcuts and Ctrl+wheel.</summary>
    public static void StepBy(double steps) => Factor += steps * Step;

    public static void Reset() => Factor = 1.0;

    /// <summary>The zoom as a whole percentage, for display and for persistence.</summary>
    public static int Percent => (int)Math.Round(_factor * 100);
}
