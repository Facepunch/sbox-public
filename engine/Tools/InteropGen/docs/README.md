# InteropGen

InteropGen generates the C++↔C# bindings for the engine. It reads `.def` files (a small DSL
describing the classes and structs that cross the native/managed boundary) and writes, for each def:

- a managed file (`cs` directive) - typed C# wrappers, `[UnmanagedCallersOnly]` exports and the
  `NativeInterop` bootstrap
- a native header (`hpp` directive) - declarations of the managed classes native can call
- a native source file (`cpp` directive) - exported thunks, import function pointers and the
  `igen_*` initializer

The generated output is **not committed** (see `.gitignore` - `Interop.*.cs` / `interop.*`); the
build regenerates it, and files whose content didn't change are left untouched.

## Documentation

| File | Contents |
|------|----------|
| [def-files.md](def-files.md) | The `.def` file format: top-level directives, includes, inherit/skipall |
| [classes.md](classes.md) | Declaring classes, structs, enums; functions, variables, attributes |
| [types.md](types.md) | Argument types, marshalling, flags (`out`, `ref`, `CastTo[...]`, ...) |

## How it runs

The build (`Tools/SboxBuild`) calls `Facepunch.InteropGen.Program.ProcessManifest( "engine" )` from
the repo root. That reads `engine/manifest.def` - a plain list of `.def` files - and processes each
listed def in parallel. Files whose content didn't change are not rewritten.

## Project layout

```
Program.cs        Entry point - reads the manifest, processes each def
Logger.cs         Per-thread buffered console output so parallel defs don't interleave
Parsers/          Line-oriented parsers for the .def DSL (global → class → inline body)
Definition/       The model: Definition, Class, Function, Variable, Struct
Arguments/        One Arg type per marshallable type - each knows its C#/C++ types and conversions
Pipeline/         InteropPipeline (parse → resolve → sort → mangle → hash), TypeResolver, Mangler
Writer/           The three emitters (ManagedWriter, NativeWriter, NativeHeaderWriter) and helpers
```

The pipeline (`Pipeline/InteropPipeline.cs`) turns a def file into a resolved `Definition`:

1. **Parse** - `GlobalParser` reads the file (and its includes) line by line
2. **Resolve** - `TypeResolver` links base classes, pulls inherited functions down, and converts
   every unknown type name into a concrete `Arg`
3. **Sort** - structs and classes are ordered by name so output is deterministic
4. **Mangle** - `Mangler` gives every function/variable a unique C-safe export name
5. **Hash** - the def text is hashed; both sides verify it at runtime so a stale binary fails fast

## Invariants - read before changing anything

The native and managed sides are built from the same generated files, and the runtime verifies the
def hash when the tables are exchanged - so a stale native/managed pairing fails fast. When
changing the generator:

- **Never** change the hash computation, the name mangling or the sort order unless both sides will
  be regenerated and rebuilt together (they normally are - the build runs the generator).
- A pure refactor should be verified by snapshot-diffing the output, since the generated files
  aren't in git: run the generator before your change, hash every output, run it again after, and
  compare.

```
# from the repo root, with a tiny console app calling Program.ProcessManifest("engine"):
dotnet run                          # before the change
<hash engine/Sandbox.*/Interop.*.cs and src/**/interop.*>   # snapshot A
dotnet run                          # after the change
<hash again>                        # snapshot B - must equal A for a pure refactor
```

Two ordering details that are easy to break:

- The native export table (casts, functions, variable get/set) is index-matched between C++ and C#.
  Both emitters drive it from `Writer/NativeExportTable.cs` - the single source of truth for slot
  order. Don't enumerate slots anywhere else.
- Mangled names de-duplicate collisions with numeric suffixes in encounter order, so reordering
  classes or members in a def can rename exports. That's fine - both sides regenerate together -
  but it's why output must always be regenerated as a pair.
