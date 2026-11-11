using System.Runtime.InteropServices;
using System.Text;

namespace Sandbox.ShaderCompiler.Slang;

/// <summary>
/// An <c>ISlangFileSystem</c> written in C#: Slang reads every source and include through the callback it's made with,
/// which gets a path and returns the text it should compile (or null when there's no such file). This is where the compiler
/// does what vfx's parser does to sources before Slang sees them.
/// </summary>
internal sealed unsafe class SlangFileSystem : IDisposable
{
	// ISlangUnknown, ISlangCastable, ISlangFileSystem
	static readonly Guid Unknown = new( 0x00000000, 0x0000, 0x0000, 0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46 );
	static readonly Guid Castable = new( 0x87ede0e1, 0x4852, 0x44b0, 0x8b, 0xf2, 0xcb, 0x31, 0x87, 0x4d, 0xe2, 0x39 );
	static readonly Guid FileSystem = new( 0x003A09FC, 0x3A4D, 0x4BA0, 0xAD, 0x60, 0x1F, 0xD8, 0x63, 0xA9, 0x15, 0xAB );

	static readonly IntPtr* vtable = CreateVtable();

	struct Object
	{
		public IntPtr* Vtable;
		public IntPtr Handle;
	}

	readonly Func<string, string> load;
	readonly Object* instance;
	GCHandle self;

	/// <summary>The COM object to hand Slang. Valid until disposed; Slang's references don't keep it alive.</summary>
	public IntPtr Pointer => (IntPtr)instance;

	public SlangFileSystem( Func<string, string> load )
	{
		this.load = load;
		self = GCHandle.Alloc( this );
		instance = (Object*)NativeMemory.Alloc( (nuint)sizeof( Object ) );
		instance->Vtable = vtable;
		instance->Handle = GCHandle.ToIntPtr( self );
	}

	static IntPtr* CreateVtable()
	{
		var table = (IntPtr*)NativeMemory.Alloc( (nuint)(5 * sizeof( IntPtr )) );
		table[0] = (IntPtr)(delegate* unmanaged< IntPtr, Guid*, IntPtr*, int >)&QueryInterface;
		table[1] = (IntPtr)(delegate* unmanaged< IntPtr, uint >)&AddRef;
		table[2] = (IntPtr)(delegate* unmanaged< IntPtr, uint >)&Release;
		table[3] = (IntPtr)(delegate* unmanaged< IntPtr, Guid*, IntPtr >)&CastAs;
		table[4] = (IntPtr)(delegate* unmanaged< IntPtr, byte*, IntPtr*, int >)&LoadFile;
		return table;
	}

	[UnmanagedCallersOnly]
	static int QueryInterface( IntPtr self, Guid* guid, IntPtr* result )
	{
		if ( *guid == Unknown || *guid == Castable || *guid == FileSystem )
		{
			*result = self;
			return 0;
		}

		*result = IntPtr.Zero;
		return unchecked((int)0x80004002); // E_NOINTERFACE
	}

	// The object's lifetime is the compiler's (Dispose), not Slang's
	[UnmanagedCallersOnly]
	static uint AddRef( IntPtr self ) => 1;

	[UnmanagedCallersOnly]
	static uint Release( IntPtr self ) => 1;

	[UnmanagedCallersOnly]
	static IntPtr CastAs( IntPtr self, Guid* guid ) => *guid == Unknown || *guid == Castable || *guid == FileSystem ? self : IntPtr.Zero;

	[UnmanagedCallersOnly]
	static int LoadFile( IntPtr self, byte* path, IntPtr* blob )
	{
		*blob = IntPtr.Zero;
		try
		{
			var system = (SlangFileSystem)GCHandle.FromIntPtr( ((Object*)self)->Handle ).Target;
			var text = system.load( SlangApi.String( path ) );
			if ( text is null ) return unchecked((int)0x80004005); // E_FAIL: not found

			var bytes = Encoding.UTF8.GetBytes( text );
			fixed ( byte* data = bytes ) *blob = SlangApi.CreateBlob( data, (nuint)bytes.Length );
			return 0;
		}
		catch ( Exception )
		{
			return unchecked((int)0x80004005);
		}
	}

	public void Dispose()
	{
		if ( !self.IsAllocated ) return;
		NativeMemory.Free( instance );
		self.Free();
	}
}
