# .def file format

A `.def` file is parsed line by line. Blank lines are skipped, and lines starting with `//` are
comments. Every other line is one of:

- a **directive** - the first word is the keyword, the rest of the line is its argument
- a **type declaration** - `native class ...`, `managed struct ...`, etc. (see [classes.md](classes.md))
- an **attribute** - `[nogc]`, `[Handle:...]`, etc., applying to the next declaration

Directive arguments may be wrapped in double quotes; the quotes are stripped. The keyword doesn't
have to start the line - `#include "foo.h"` works because the parser matches the first word-run it
finds, which is handy for keeping C++ syntax highlighting happy in def files.

## Top-level directives

| Directive | Example | Meaning |
|-----------|---------|---------|
| `ident` | `ident "engine"` | Name of this binding set. Becomes the native init export `igen_engine` and namespaces handle types. |
| `nativedll` | `nativedll engine2.dll` | The native dll managed loads at startup (extension is stripped; resolved per-platform). |
| `cs` | `cs "../Sandbox.Engine/Interop.Engine.cs"` | Where to write the managed output, relative to the def file. |
| `cpp` | `cpp "../../src/engine2/interop.engine.cpp"` | Where to write the native source. |
| `hpp` | `hpp "../../src/engine2/interop.engine.h"` | Where to write the native header. Per-class sub-headers (`interop.engine.<class>.h`) are written next to it. |
| `namespace` | `namespace "Managed.SandboxEngine"` | The namespace for the generated `NativeInterop`/`Exports` classes - C# namespace on the managed side, C++ namespace on the native side. |
| `exceptions` | `exceptions "Sandbox.Interop.BindingException"` | Managed method called when an exported managed function throws: `(string className, string functionName, Exception e)`. |
| `pch` | `pch "cbase.h"` | Precompiled header `#include`d at the top of the generated .cpp. |
| `include` | `include "engine/*"` | Includes a header, def file or folder - see below. |
| `inherit` | `inherit "engine.def"` | This binding set builds on another - see below. |
| `skipall` | `skipall "tools.def"` | Skip everything another def already covers - see below. |
| `delegate` | `delegate DebugDrawDelegate_t;` | Declares a native delegate type name so it can be used as a parameter type (passed as `IntPtr`, converted with `FunctionPointerToDelegate<T>` on the native side). |

## include

`include` does three different things depending on its argument:

- ends in `.h` → emitted as `#include "..."` in the generated native header. The conventional
  spelling is `#include "vphysics2/iphysicsbody.h"`, which parses identically.
- ends in `.def` → that file is parsed inline, as if its contents were pasted here. Paths are
  relative to the including file.
- otherwise → treated as a folder: every `*.def` in it is included in alphabetical order.
  A trailing `/*` (e.g. `include "common/*"`) recurses into subfolders.

Included def text counts toward the definition hash, so a change in any included file correctly
invalidates the binary pairing.

## inherit and skipall

Several defs can cover overlapping native code. `tools.def` builds on `engine.def`:

```
ident "tools"
inherit "engine.def"
include "common/*"      // same folders engine.def includes
```

`inherit` loads the other def and remembers what it declared. Types declared there are still
*parsed* here (the includes overlap on purpose) so they can be referenced as parameter and return
types - but they are **not re-emitted**: their wrappers already exist in the inherited def's
assembly, and they're excluded from this def's export tables and struct size checks.

`skipall` is the stronger form used when a def chain gets deep (e.g. `tools.hammer.def` inherits
`engine.def`, `tools.def` and `tools.assetsystem.def`):

```
inherit "tools.def"
skipall "tools.def"
```

Everything that appears in the named def - classes (native *and* managed), structs, and its `.h`
includes - is skipped during emission here.

## manifest.def

`engine/manifest.def` is just the list of defs to build, one per line:

```
Definitions/tools.def
Definitions/engine.def
...
```

Lines not ending in `.def` are ignored.
