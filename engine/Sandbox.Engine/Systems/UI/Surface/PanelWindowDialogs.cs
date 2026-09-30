using NativeEngine;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Sandbox.UI;

/// <summary>
/// SDL file dialogs, with one request at a time. Results return to the main thread before completing the task.
/// </summary>
internal static class PanelWindowDialogs
{
	static Request pending;

	internal static Task<string> PickFolder( IntPtr window, string defaultPath ) => Show( window, defaultPath, null, folder: true );
	internal static Task<string> PickOpenFile( IntPtr window, string defaultPath, string filters ) => Show( window, defaultPath, filters );
	internal static Task<string> PickSaveFile( IntPtr window, string defaultPath, string filters ) => Show( window, defaultPath, filters, save: true );

	static unsafe Task<string> Show( IntPtr window, string defaultPath, string filters, bool folder = false, bool save = false )
	{
		ThreadSafe.AssertIsMainThread();
		// Treat another request like cancellation; it must never receive the first dialog's selection.
		if ( pending is not null ) return Task.FromResult<string>( null );

		var request = new Request( defaultPath, filters );
		pending = request;
		try
		{
			var callback = (IntPtr)(delegate* unmanaged[Cdecl]< IntPtr, IntPtr, int, void >)&OnResult;
			var userdata = GCHandle.ToIntPtr( request.Handle );
			if ( folder )
				Sdl.ShowOpenFolderDialogAt( callback, userdata, window, request.DefaultPath );
			else if ( save )
				Sdl.ShowSaveFileDialog( callback, userdata, window, (IntPtr)request.Filters, request.FilterCount, request.DefaultPath );
			else
				Sdl.ShowOpenFileDialog( callback, userdata, window, (IntPtr)request.Filters, request.FilterCount, request.DefaultPath, false );
		}
		catch
		{
			pending = null;
			request.Dispose();
			throw;
		}
		// SDL may invoke the callback before Show returns, so keep this request's task, not pending's.
		return request.Completion.Task;
	}

	[UnmanagedCallersOnly( CallConvs = [typeof( CallConvCdecl )] )]
	static void OnResult( IntPtr userdata, IntPtr files, int filter )
	{
		try
		{
			var request = (Request)GCHandle.FromIntPtr( userdata ).Target;
			string path = null;
			try
			{
				if ( files != IntPtr.Zero ) path = Marshal.PtrToStringUTF8( Marshal.ReadIntPtr( files ) );
			}
			finally
			{
				request.Dispose();
				MainThread.Queue( () =>
				{
					pending = null;
					request.Completion.TrySetResult( string.IsNullOrEmpty( path ) ? null : path );
				} );
			}
		}
		catch ( Exception e ) { Log.Warning( e, "Panel window file dialog callback failed" ); }
	}

	/// <summary>
	/// SDL retains filters and paths until the callback. Keep their UTF-8 storage and callback context alive with the request.
	/// </summary>
	sealed unsafe class Request : IDisposable
	{
		internal readonly TaskCompletionSource<string> Completion = new();
		internal GCHandle Handle;
		internal IntPtr DefaultPath;
		internal Sdl.DialogFileFilter* Filters;
		internal int FilterCount;

		internal Request( string defaultPath, string filters )
		{
			try
			{
				DefaultPath = string.IsNullOrEmpty( defaultPath ) ? IntPtr.Zero : Marshal.StringToCoTaskMemUTF8( defaultPath );
				// Name and extension-list pairs: "Scene files|scene;prefab|All files|*".
				var parts = filters?.Split( '|' ) ?? [];
				FilterCount = parts.Length / 2;
				if ( FilterCount > 0 )
				{
					Filters = (Sdl.DialogFileFilter*)NativeMemory.AllocZeroed( (nuint)FilterCount, (nuint)sizeof( Sdl.DialogFileFilter ) );
					for ( int i = 0; i < FilterCount; i++ )
					{
						Filters[i].Name = Marshal.StringToCoTaskMemUTF8( parts[i * 2] );
						Filters[i].Pattern = Marshal.StringToCoTaskMemUTF8( parts[i * 2 + 1] );
					}
				}
				Handle = GCHandle.Alloc( this );
			}
			catch
			{
				Dispose();
				throw;
			}
		}

		public void Dispose()
		{
			if ( Filters != null )
			{
				for ( int i = 0; i < FilterCount; i++ )
				{
					Marshal.FreeCoTaskMem( Filters[i].Name );
					Marshal.FreeCoTaskMem( Filters[i].Pattern );
				}
				NativeMemory.Free( Filters );
				Filters = null;
			}
			Marshal.FreeCoTaskMem( DefaultPath );
			DefaultPath = IntPtr.Zero;
			if ( Handle.IsAllocated ) Handle.Free();
		}
	}
}
