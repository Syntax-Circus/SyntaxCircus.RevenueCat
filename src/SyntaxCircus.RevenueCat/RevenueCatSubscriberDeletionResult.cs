namespace SyntaxCircus.RevenueCat;

/// <summary>Outcome of <see cref="IRevenueCatSubscriberDeletionClient.DeleteSubscriberAsync"/>.</summary>
public enum RevenueCatSubscriberDeletionResult
{
    /// <summary>The subscriber was deleted.</summary>
    Deleted,

    /// <summary>
    /// The subscriber didn't exist (already deleted, or never existed). Treated as a successful,
    /// idempotent outcome rather than an error - deleting something that's already gone is the
    /// desired end state.
    /// </summary>
    NotFound,
}
