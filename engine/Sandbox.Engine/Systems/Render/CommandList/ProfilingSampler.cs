using System.Runtime.InteropServices;

namespace Sandbox.Rendering;

/// <summary>
/// A named GPU profiling marker.
/// </summary>
/// <example>
/// <code>
/// static readonly ProfilingSampler MyProfilingScope = new( "My Profiling Scope" );
///
/// using ( _commandList.ProfileScope( MyProfilingScope ) )
/// {
///     // do something here...
/// }
/// </code>
/// </example>
public sealed class ProfilingSampler
{
	/// <summary>
	/// Name shown in the GPU profiler overlay and in graphics debuggers
	/// </summary>
	public string Name { get; }

	private IntPtr _namePtr;

	internal IntPtr NamePtr => _namePtr;

	public ProfilingSampler( string name )
	{
		Name = string.IsNullOrWhiteSpace( name ) ? "Scope" : name;
		_namePtr = Marshal.StringToCoTaskMemUTF8( Name );
	}

	~ProfilingSampler()
	{
		if ( _namePtr != IntPtr.Zero )
		{
			Marshal.FreeCoTaskMem( _namePtr );
			_namePtr = IntPtr.Zero;
		}
	}

	public override string ToString() => Name;
}
