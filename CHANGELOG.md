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
