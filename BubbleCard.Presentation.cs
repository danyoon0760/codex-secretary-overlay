using System.Windows;
using System.Windows.Controls;

namespace SecretaryOverlay;

internal sealed partial class BubbleCard
{
    internal const long EntranceMs = 150;
    private readonly TextBlock displayedBody = PetTypography.Text("", 17);
    private readonly BubbleTextReveal textReveal = new();
    private long presentationIdentity;
    private long? entranceStarted;
    private bool waitingForSpace;
    internal bool PresentationComplete => !waitingForSpace && entranceStarted is null && textReveal.IsComplete;
    internal string DisplayedText => Body.Opacity == 1 ? Body.Text : displayedBody.Text;

    private void InitializePresentation(Grid host)
    {
        displayedBody.TextWrapping = TextWrapping.Wrap;
        displayedBody.Margin = Body.Margin;
        displayedBody.IsHitTestVisible = false;
        displayedBody.Visibility = Visibility.Collapsed;
        host.Children.Add(displayedBody);
        SyncPresentationFont();
    }

    private void SyncPresentationFont()
    {
        displayedBody.FontFamily = Body.FontFamily;
        displayedBody.FontWeight = Body.FontWeight;
        displayedBody.FontSize = Body.FontSize;
        displayedBody.LineHeight = Body.LineHeight;
    }

    internal bool PreparePresentation(long identity, long time, bool animate)
    {
        bool fresh = presentationIdentity != identity;
        presentationIdentity = identity;
        if (!animate) { FinishPresentation(time); return fresh; }
        if (fresh)
        {
            // The complete text reserves its final height while the visible copy is revealed.
            waitingForSpace = true;
            entranceStarted = null;
            Surface.Opacity = 0;
            Surface.IsHitTestVisible = false;
            textReveal.SetTarget("", time, false);
            displayedBody.Text = "";
            displayedBody.Visibility = Visibility.Visible;
            Body.Opacity = 0;
        }
        else if (!waitingForSpace && entranceStarted is null)
        {
            textReveal.SetTarget(Body.Text, time, true);
            DisplayRevealedText(time);
        }
        return fresh;
    }

    internal void AdvancePresentation(long time, bool moving, bool animate)
    {
        if (!animate) { FinishPresentation(time); return; }
        if (waitingForSpace)
        {
            if (moving) return;
            waitingForSpace = false;
            entranceStarted = time;
        }
        if (entranceStarted is { } started)
        {
            double progress = Math.Clamp((double)(time - started) / EntranceMs, 0, 1);
            Surface.Opacity = 1 - Math.Pow(1 - progress, 3);
            if (progress < 1) return;
            entranceStarted = null;
            Surface.IsHitTestVisible = true;
            textReveal.SetTarget(Body.Text, time, true);
        }
        DisplayRevealedText(time);
    }

    private void DisplayRevealedText(long time)
    {
        displayedBody.Text = textReveal.Advance(time);
        Body.Opacity = textReveal.IsComplete ? 1 : 0;
        displayedBody.Visibility = textReveal.IsComplete ? Visibility.Collapsed : Visibility.Visible;
    }

    internal void FinishPresentation(long time)
    {
        waitingForSpace = false;
        entranceStarted = null;
        textReveal.SetTarget(Body.Text, time, false);
        Surface.Opacity = 1;
        Surface.IsHitTestVisible = true;
        DisplayRevealedText(time);
    }
}
