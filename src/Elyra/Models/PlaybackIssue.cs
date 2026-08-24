namespace Elyra.Models;

/// <summary>A user-facing playback problem that remains visible until dismissed.</summary>
public sealed record PlaybackIssue(string Message, bool CanRetry);
