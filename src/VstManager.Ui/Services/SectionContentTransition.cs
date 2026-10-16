using Avalonia;
using Avalonia.Animation;

namespace VstManager.Ui.Services;

/// <summary>
/// The library sections' Expander ContentTransition: the cards fade in while sliding a short
/// way down from under the header, and fade out drifting back up. Avalonia's own PageSlide moves
/// the content by its full height — over 3,000 px for a large section, a blur rather than a
/// slide — so this keeps the native ContentTransition hook with a fixed small offset instead.
/// Reduce motion turns it into a plain fade (Motion handles that).
/// </summary>
public sealed class SectionContentTransition : IPageTransition
{
    public double Offset { get; set; } = 12;

    public async Task Start(Visual? from, Visual? to, bool forward, CancellationToken cancellationToken)
    {
        var running = new List<Task>(2);
        if (from is not null)
        {
            running.Add(Motion.Conceal(from, dy: -Offset * 0.6, durationMs: 170, token: cancellationToken));
        }
        if (to is not null)
        {
            to.IsVisible = true;
            running.Add(Motion.Reveal(to, dy: -Offset, durationMs: 240, token: cancellationToken));
        }

        await Task.WhenAll(running);

        // The transition contract: the outgoing visual ends hidden.
        if (from is not null && !cancellationToken.IsCancellationRequested)
        {
            from.IsVisible = false;
        }
    }
}
