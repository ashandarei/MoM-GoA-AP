namespace MoMController;

/// <summary>Orders result publication before a single deferred, game-owned quit.</summary>
internal sealed class CompletionExit
{
    private string? pendingEvent;
    private bool requested;

    internal void Publish(string eventId, bool autoClose, Action write)
    {
        // A failed write must leave both the result queue and exit state retryable.
        write();
        if (autoClose && !requested) pendingEvent ??= eventId;
    }

    internal void Dispatch(Action<string> quit)
    {
        if (requested || pendingEvent == null) return;
        var eventId = pendingEvent;
        pendingEvent = null;
        // Quit can synchronously flush results again. Never dispatch twice,
        // including when the native call throws after beginning shutdown.
        requested = true;
        quit(eventId);
    }
}
