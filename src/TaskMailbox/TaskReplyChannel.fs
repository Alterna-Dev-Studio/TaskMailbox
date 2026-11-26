namespace TaskMailbox

/// <summary>A channel for replying to a request built by <c>PostAndAsyncReply</c> or <c>PostAndTryAsyncReply</c>.</summary>
/// <remarks>
/// The constructor is public so an agent body can be unit-tested without a mailbox: the channel
/// passes every <c>Reply</c> through to <paramref name="reply"/>. For a channel created by a request,
/// only the first reply reaches the caller; replies after the caller stopped waiting are dropped silently.
/// </remarks>
[<Sealed>]
type TaskReplyChannel<'Reply>(reply: 'Reply -> unit) =

    /// <summary>Sends a reply.</summary>
    /// <param name="value">The reply value.</param>
    member _.Reply(value: 'Reply) = reply value
