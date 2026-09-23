namespace SecretaryOverlay;

internal enum ProgressKind { Commentary, Summary, Activity, FinalAnswer }
internal sealed record ProgressMessage(string Session, string Turn, string Id, string Text, DateTimeOffset At,
    ProgressKind Kind = ProgressKind.Commentary, bool CompletesTurn = false);
