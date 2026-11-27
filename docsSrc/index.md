## What is TaskMailbox?

TaskMailbox provides `TaskMailboxProcessor<'Msg>`, a [MailboxProcessor](https://fsharp.github.io/fsharp-core-docs/reference/fsharp-control-fsharpmailboxprocessor-1.html) whose body is an IcedTasks `cancellableTask`.

## Install

```sh
dotnet add package TaskMailbox
```

Then open the namespaces:

```fsharp
open IcedTasks
open TaskMailbox
```

## Learn the concepts

- [Compatibility with MailboxProcessor](Explanations/Compatibility.html)
- [Choosing TaskMailbox](Explanations/Choosing-TaskMailbox.html)
- [Performance baseline](Explanations/Performance.html)

If you need API signatures, generated reference documentation is available under [API Reference](reference/index.html).
