# Argument types and flags

Every parameter, return value and variable in a def has a type. A type is either one of the
built-ins below, or a name resolved against the def's declared classes, structs, enums, pointers
and delegates.

## Built-in types

Built-ins are `Arg` subclasses in `Arguments/`, registered by `[TypeName]` attribute (matching is
case-insensitive).

| .def type | C# | C++ | Notes |
|-----------|----|-----|-------|
| `void` | `void` | `void` | Return type only. |
| `bool` | `bool` | `bool` | Crosses the boundary as `int` (0/1). |
| `byte` | `byte` | `unsigned char` | |
| `ushort` | `ushort` | `unsigned short` | |
| `int` | `int` | `int` | |
| `uint` | `uint` | `unsigned int` | |
| `long` | `long` | `int64` | |
| `ulong` | `ulong` | `uint64` | |
| `float` | `float` | `float` | |
| `double` | `double` | `double` | |
| `intptr`, `void*` | `IntPtr` | `void*` | Raw pointer. |
| `string` | `string` | `const char*` | UTF-8. Pinned for the call going in; copied to a thread-local on the way out (`SafeReturnString`). |
| `CUtlString` | `string` | `CUtlString` | Like `string` but the native side holds a `CUtlString`. |
| `stringtoken` | `Sandbox.StringToken` | `uint32` | Built native-side with `StringTokenFromHashCode`. |

Qt types, used by the tools defs:

| .def type | C# | C++ |
|-----------|----|-----|
| `qstring` | `string` | `QString` (crosses as `const QChar*`) |
| `qdir` | `string` | `QDir` |
| `qbytearray` | `string` | `QByteArray` (crosses base64-encoded) |
| `qicon` | `string` | resolved with `FindOrCreateQIcon` |
| `qreal` | `float` | `qreal` |
| `qpoint`, `qpointf` | `Vector3` | `QPoint` / `QPointF` |
| `qsize`, `qsizef` | `Vector3` | `QSize` / `QSizeF` |
| `qcolor` | `Color32` | `QColor` |
| `qrect`, `qrectf` | `QRectF` | `QRectF` |

## Resolving everything else

Any other type name is looked up, in order:

1. a declared **class** by managed name with namespace, then by short managed name
2. a declared **struct/enum/pointer** by managed name (with or without namespace), then native name
3. a declared **delegate**

An unknown type fails the build with `Unknown Type <name>`.

How a resolved type crosses the boundary depends on what it is:

- **native class** - the raw native pointer (`IntPtr` in C#). `[Handle:...]` classes cross as a
  handle id, `[ResourceHandle:...]` classes as a strong-handle pointer.
- **managed class** - a uint object id, resolved through `Sandbox.InteropSystem`.
- **struct** - by pointer if 8+ bytes, by value if marked `[small]`.
- **enum** - as a 64-bit integer, cast on both sides.
- **pointer** - as an opaque `IntPtr`.
- **delegate** - as an `IntPtr`, converted native-side with `FunctionPointerToDelegate<T>`.

## Parameter flags

Flags are written before the type:

```
void GetEngineSwapChainSize( out int w, out int h );
void SetTonemapParameters( ref SceneTonemapParameters_t pParams );
bool ConvertImageFormat( CastTo[uint8*] void* src, ... );
```

| Flag | Sides | Meaning |
|------|-------|---------|
| `out` | C# `out` param | Passed to native as a pointer to write through. `out string` works too - the native side writes a pointer, managed converts it after the call. |
| `ref` | C# `ref` param | Passed as a pointer, readable and writable. |
| `asref` | return values | The wrapper returns `ref T` - a managed ref into native memory (`Unsafe.AsRef`). |
| `cref` | native side | The native method wants a reference (`T&`): the thunk dereferences the incoming pointer, and outgoing calls pass `&value`. |
| `cast` | native side | Cast the value to the parameter's native type when passing it. |
| `CastTo[X]` | native side | Cast the incoming value to the exact native type `X` before use - for types managed doesn't model (e.g. `CastTo[CKV3MemberName] string key`). |
| `stable` | `string` returns | Promises the returned `const char*` outlives the call (an interned name, a member that isn't going away), so the thunk skips the defensive copy into a thread-local that string returns normally get. Only valid on `string` - don't use it for pointers into temporaries. |

## Arrays and literals

- `type[] name` - passes a raw pointer to the first element (`type*` on both sides). The caller
  guarantees the length by convention (usually a separate count parameter).
- `[expression]` as a parameter is a literal: it isn't part of the managed signature at all, and the
  native call site passes the expression verbatim.
