# Classes, structs and members

## Declaring classes

```
native class IPhysicsBody as NativeEngine.IPhysicsBody
{
	void SetMass( float f );
	float GetMass();
}
```

Every class declaration starts with `native` or `managed` - which side *implements* it:

- **`native class`** - implemented in C++, imported into managed. The generator emits a C# wrapper
  struct holding the native pointer, with a method per function.
- **`managed class`** - implemented in C#, exported to native. The generator emits a C++ class
  (in the native header) whose methods call into managed, and `[UnmanagedCallersOnly]` thunks on
  the managed side. Instances are addressed by an object id resolved via `Sandbox.InteropSystem`.

The forms:

| Form | Meaning |
|------|---------|
| `native class X` | Instance class - wrapper carries a `self` pointer. |
| `native static class X` | No instances; members are called as `X::Member()`. A static class whose native name starts with `global` binds free functions at global scope (`::Member()`). |
| `native accessor g_pThing` | Like a static class, but members are reached through a global pointer: `g_pThing->Member()`. The generated thunk asserts the pointer isn't null. |
| `managed class X` / `managed static class X` | Same split, implemented in C#. |

### Names and aliasing

`as` gives the type a different name on the other side:

```
native class CVideoPlayer as NativeEngine.CVideoPlayer
managed static class Sandbox.Engine.Bootstrap
```

The first name is the implementing side's name, the `as` name is the other side's. Namespaces use
`.` (or `::` for native) and are split off automatically. Without `as`, both sides use the same
name.

### Base classes

```
native class CSceneObject as Sandbox.SceneObject
native class CSceneModel as Sandbox.SceneModel : Sandbox.SceneObject
```

`: Base` references another class *in the same def*. The derived wrapper inherits all of the base's
non-static functions and gets implicit/explicit conversion operators to and from every base. The
conversions go through native `dynamic_cast`, because with multiple inheritance base pointers can
differ from derived pointers.

## Functions

```
bool Play( string pUrl, string pExt );
static CVideoPlayer Create( Sandbox.VideoPlayer managedObject ); [new]
void Destroy(); [delete]
int GetMemRequired( int width, int height ) const;
```

- `static` members don't take a `self`/instance argument.
- A trailing `const` is accepted (and required to match a const native method on managed classes).
- Overloads are fine - mangled export names are de-duplicated automatically.
- A function named `GetType` is emitted as `GetType_Native` in C# (to avoid `object.GetType`).

**Specials** after the semicolon:

| Special | Meaning |
|---------|---------|
| `[new]` | The thunk runs `return new NativeType( args );` instead of calling a method. |
| `[delete]` | The thunk runs `delete (NativeType*)self;`. On the managed side the wrapper also nulls its own pointer afterwards. |

### Inline functions

A function starting with `inline` carries its native body in the def file, instead of binding to an
existing native method:

```
inline void SetBoneName( string boneName )
{
	self->m_boneName = boneName;
	self->m_pBoneName = self->m_boneName.String();
}
```

The body is emitted into the generated .cpp. Parameters arrive already converted to their native
types under their declared names (`self` is the typed instance pointer; `this->` is rewritten to
`self->`). `return` expressions are marshalled like a normal return value.

## Variables

A field declaration generates a get/set thunk pair and a C# property:

```
native class SheetSequence_t
{
	uint m_nId;
	bool m_bClamp;
	float m_flTotalTime;
}
```

## Structs, enums and pointer handles

Value types that cross the boundary are declared at file level:

```
native struct MeshTraceInput
native enum ButtonCode_t is NativeEngine.ButtonCode;
native pointer SwapChainHandle_t;
```

- **`struct`** - both sides must define the type with an identical layout. Sizes are verified at
  startup (`structSizes` exchange); a mismatch is a fatal error. Structs 8 bytes or larger are
  passed by pointer when used as parameters; smaller structs must be marked `[small]` and are
  passed by value.
- **`enum`** - passed as a 64-bit integer and cast on both sides.
- **`pointer`** - a native type that pretends not to be a pointer (`DECLARE_POINTER_HANDLE`).
  Managed treats it as an opaque `IntPtr`-sized struct.

`as` / `is` (synonyms) alias the managed name, same as for classes.

## Attributes

Attributes are written on the line(s) before a declaration and apply to the next class, struct or
function parsed.

### On functions

| Attribute | Meaning |
|-----------|---------|
| `[nogc]` | Emits `[SuppressGCTransition]` - the call skips the GC transition, so it must be short, non-blocking and never call back into managed. On a class, applies to all of its functions. |
| `[callback]` | Marks a function that can re-enter managed. Cancels `[nogc]` inherited from the class. |

### On classes

| Attribute | Meaning |
|-----------|---------|
| `[Handle:Managed.Type]` | Instances cross the boundary as handle ids instead of raw pointers. Managed resolves them with `Sandbox.HandleIndex.Get<T>`, native with `GetManagedHandle`. |
| `[ResourceHandle:HModel]` | The class is accessed through a strong resource handle (`HModelStrong`). Thunks dereference the handle (returning default if unloaded), and the class automatically gains handle-management functions: `DestroyStrongHandle`, `IsStrongHandleValid`, `IsError`, `IsStrongHandleLoaded`, `CopyStrongHandle`, `GetBindingPtr`. |
| `[SharedDataPointer]` | For Qt-style classes that are just a smart pointer to shared data. The wrapper is a public class (not a struct) that copies by value across the boundary and queues a `Dispose` from its finalizer. |
| `[WindowsOnly]` | When generating on a non-Windows platform, the native thunks are emitted as stubs returning default values. |

### On structs

| Attribute | Meaning |
|-----------|---------|
| `[small]` | The struct is smaller than a pointer (8 bytes) and is passed by value. Required - the generated code `static_assert`s that unmarked structs are at least 8 bytes. |
