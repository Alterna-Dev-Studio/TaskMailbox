# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- `TaskMailboxProcessor<'Msg>` with `Post`, `Receive`, `TryReceive`, `Start`, `StartImmediate`, `DefaultTimeout`, `CurrentQueueLength`, `Error`, and `Dispose`
- `Scan` and `TryScan` for selective receive
- `mailbox { }` computation expression whose `return!` is a tail call, for recursive agent loops
- Compatibility tests that run the same scenarios against FSharp.Core's `MailboxProcessor`
- `TaskReplyChannel<'Reply>` and `PostAndReply`, `TryPostAndReply`, `PostAndAsyncReply`, `PostAndTryAsyncReply` for request/reply

### Changed
- Faster `Receive`/`TryReceive` on a non-empty queue; cheaper cancellation and timeout
- `StartImmediate` no longer resumes the body on the caller's `SynchronizationContext`
- Cancelling a pending `Receive` or `Scan` no longer throws inside the library

### Fixed
- A request that races `Dispose` fails with `ObjectDisposedException` from all four request members instead of being silently dropped
