using System.Runtime.CompilerServices;
using System.Threading;

namespace Sandbox.Diagnostics;

/// <summary>
/// A named region for profiler captures. Declare one as a static readonly field and time work with <c>using var scope = Scope.Start();</c>.
/// Name a fixed operation, not an object. Costs a flag check while nothing profiles.
/// </summary>
public sealed class ProfileScope
{
	readonly string _name, _file;
	readonly int _line;
	int _id;

	public ProfileScope( string name, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0 )
	{
		ArgumentException.ThrowIfNullOrEmpty( name );
		// Bounded so names can't bloat the process-wide scope registry or saved captures
		_name = name.Length > 256 ? name[..256] : name;
		_file = file?.Length > 1024 ? file[^1024..] : file;
		_line = line;
	}

	/// <summary>Begin a scope on this thread. Never carry it across an await or to another thread.</summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public Instance Start()
	{
		if ( !Profiler.ScopesActive ) return default;
		var id = Volatile.Read( ref _id );
		if ( id == 0 ) id = Register();
		if ( id < 0 ) return default;
		if ( !t_attached && Profiler.IsRecording && !Attach() ) return default;
		return new( NativeEngine.Profiler.Begin( (uint)id ) );
	}

	[ThreadStatic] static bool t_attached;

	// Begin runs without a GC transition, so a recording thread's ring is claimed and allocated here first
	[MethodImpl( MethodImplOptions.NoInlining )]
	static bool Attach() => t_attached = NativeEngine.Profiler.AttachThread();

	[MethodImpl( MethodImplOptions.NoInlining )]
	int Register()
	{
		// Native registration deduplicates racing threads and hotloaded copies
		var id = (int)NativeEngine.Profiler.RegisterScope( _name, _file, _line );
		if ( id == 0 ) id = -1; // Registry full, don't retry every time
		Volatile.Write( ref _id, id );
		return id;
	}

	/// <summary>An open scope, disposed once on the thread that started it.</summary>
	public ref struct Instance
	{
		ulong _token;
		internal Instance( ulong token ) { _token = token; }
		[MethodImpl( MethodImplOptions.AggressiveInlining )]
		public void Dispose()
		{
			if ( _token == 0 ) return;
			NativeEngine.Profiler.End( _token );
			_token = 0;
		}
	}
}
