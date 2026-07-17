using Sandbox;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Editor;

/// <summary>
/// Recent projects for the Windows taskbar jump list - the menu you get right clicking the taskbar
/// icon, like Visual Studio's recent solutions.
/// </summary>
public static class TaskbarJumpList
{
	const int MaxItems = 10;

	/// <summary>
	/// Rebuild the recent projects list from addons.json. Called on launch and when we open a project.
	/// </summary>
	public static void Refresh()
	{
		if ( !OperatingSystem.IsWindows() )
			return;

		try
		{
			var entries = new ProjectList().GetAll()
				.Where( x => !x.IsBuiltIn )
				.OrderByDescending( x => x.LastOpened )
				.Select( x => new RecentProject( TitleOf( x ), x.ConfigFilePath ) )
				.ToList();

			Windows.Rebuild( entries );
		}
		catch ( Exception e )
		{
			Console.Error.WriteLine( $"Couldn't update taskbar jump list: {e.Message}" );
		}
	}

	static string TitleOf( Project project )
		=> string.IsNullOrWhiteSpace( project.Config?.Title )
			? Path.GetFileNameWithoutExtension( project.ConfigFilePath )
			: project.Config.Title;

	readonly record struct RecentProject( string Title, string ConfigFilePath );

	// All the shell COM stuff lives here. Windows only, callers guard with OperatingSystem.IsWindows().
	[SupportedOSPlatform( "windows" )]
	static class Windows
	{
		const ushort VT_LPWSTR = 31;

		public static void Rebuild( IReadOnlyList<RecentProject> projects )
		{
			// sbox-dev.exe sits next to us in the game folder. Entries launch it directly so clicking
			// one opens the project without bouncing through the launcher.
			var gameDir = Path.GetDirectoryName( Environment.ProcessPath );
			if ( string.IsNullOrEmpty( gameDir ) )
				return;

			var editorExe = Path.Combine( gameDir, "sbox-dev.exe" );

			// No SetAppID on purpose. We commit under our own implicit (path derived) appid, which is
			// the same one the taskbar uses for a pin to this exe, so it works on pins that already exist.
			var list = (ICustomDestinationList)new CDestinationList();

			// BeginList gives us the slot count and the entries the user removed by hand. Adding a removed
			// one back makes CommitList throw, so we skip those. maxSlots is 0 when jump lists are turned
			// off in taskbar settings, which just falls through to committing nothing.
			var iidObjectArray = typeof( IObjectArray ).GUID;
			list.BeginList( out uint maxSlots, ref iidObjectArray, out object removedObj );
			var removedArgs = GetRemovedArguments( removedObj as IObjectArray );

			var cap = Math.Min( MaxItems, (int)maxSlots );

			var links = new List<IShellLinkW>();
			foreach ( var project in projects )
			{
				if ( links.Count >= cap )
					break;

				if ( string.IsNullOrWhiteSpace( project.ConfigFilePath ) )
					continue;

				var args = MakeArguments( project.ConfigFilePath );
				if ( removedArgs.Contains( args ) )
					continue;

				links.Add( CreateShellLink( editorExe, gameDir, project.Title, args, project.ConfigFilePath ) );
			}

			// Nothing to show, clear whatever was there before.
			if ( links.Count == 0 )
			{
				list.AbortList();
				try { list.DeleteList( null ); } catch { }
				return;
			}

			var collection = (IObjectCollection)new CEnumerableObjectCollection();
			foreach ( var link in links )
				collection.AddObject( link );

			list.AppendCategory( "Recent Projects", (IObjectArray)collection );
			list.CommitList();
		}

		static string MakeArguments( string configFilePath ) => $"-project \"{configFilePath}\"";

		static IShellLinkW CreateShellLink( string editorExe, string workingDir, string title, string arguments, string tooltipPath )
		{
			var link = (IShellLinkW)new CShellLink();
			link.SetPath( editorExe );
			link.SetArguments( arguments );
			link.SetWorkingDirectory( workingDir );
			link.SetIconLocation( editorExe, 0 );
			link.SetDescription( tooltipPath ); // tooltip

			// The label in a custom category comes from System.Title, not the description. Have to build
			// the PROPVARIANT by hand - InitPropVariantFromString is a header inline, not a real
			// propsys.dll export, so you can't p/invoke it.
			var store = (IPropertyStore)link;
			var pv = new PROPVARIANT { vt = VT_LPWSTR, p = Marshal.StringToCoTaskMemUni( title ?? "" ) };
			try
			{
				var key = PKEY_Title;
				store.SetValue( ref key, ref pv );
				store.Commit();
			}
			finally
			{
				PropVariantClear( ref pv );
			}

			return link;
		}

		// The args of anything the user removed from the list by hand, so we don't just add it back.
		static HashSet<string> GetRemovedArguments( IObjectArray removed )
		{
			var result = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
			if ( removed is null )
				return result;

			removed.GetCount( out uint count );
			var iid = typeof( IShellLinkW ).GUID;

			for ( uint i = 0; i < count; i++ )
			{
				try
				{
					removed.GetAt( i, ref iid, out object obj );
					if ( obj is IShellLinkW link )
					{
						var sb = new StringBuilder( 1024 );
						link.GetArguments( sb, sb.Capacity );
						result.Add( sb.ToString() );
					}
				}
				catch
				{
				}
			}

			return result;
		}

		// System.Title
		static PROPERTYKEY PKEY_Title => new()
		{
			fmtid = new Guid( "F29F85E0-4FF9-1068-AB91-08002B27B3D9" ),
			pid = 2
		};

		[DllImport( "ole32.dll" )]
		static extern int PropVariantClear( ref PROPVARIANT pvar );

		// COM interop

		[StructLayout( LayoutKind.Sequential )]
		struct PROPERTYKEY
		{
			public Guid fmtid;
			public uint pid;
		}

		// 24 bytes on x64, 16 on x86. We only ever put a string in it, but the shell writes the whole
		// struct so the trailing padding has to be here.
		[StructLayout( LayoutKind.Sequential )]
		struct PROPVARIANT
		{
			public ushort vt;
			public ushort r1;
			public ushort r2;
			public ushort r3;
			public IntPtr p;
			public IntPtr p2;
		}

		[ComImport, Guid( "6332DEBF-87B5-4670-90C0-5E57B408A49E" )]
		[InterfaceType( ComInterfaceType.InterfaceIsIUnknown )]
		interface ICustomDestinationList
		{
			void SetAppID( [MarshalAs( UnmanagedType.LPWStr )] string pszAppID );
			void BeginList( out uint pcMaxSlots, ref Guid riid, [MarshalAs( UnmanagedType.IUnknown )] out object ppv );
			void AppendCategory( [MarshalAs( UnmanagedType.LPWStr )] string pszCategory, [MarshalAs( UnmanagedType.Interface )] IObjectArray poa );
			void AppendKnownCategory( int category );
			void AddUserTasks( [MarshalAs( UnmanagedType.Interface )] IObjectArray poa );
			void CommitList();
			void GetRemovedDestinations( ref Guid riid, [MarshalAs( UnmanagedType.IUnknown )] out object ppv );
			void DeleteList( [MarshalAs( UnmanagedType.LPWStr )] string pszAppID );
			void AbortList();
		}

		[ComImport, Guid( "92CA9DCD-5622-4BBA-A805-5E9F541BD8C9" )]
		[InterfaceType( ComInterfaceType.InterfaceIsIUnknown )]
		interface IObjectArray
		{
			void GetCount( out uint pcObjects );
			void GetAt( uint uiIndex, ref Guid riid, [MarshalAs( UnmanagedType.IUnknown )] out object ppv );
		}

		[ComImport, Guid( "5632B1A4-E38A-400A-928A-D4CD63230295" )]
		[InterfaceType( ComInterfaceType.InterfaceIsIUnknown )]
		interface IObjectCollection
		{
			// IObjectArray
			void GetCount( out uint pcObjects );
			void GetAt( uint uiIndex, ref Guid riid, [MarshalAs( UnmanagedType.IUnknown )] out object ppv );
			// IObjectCollection
			void AddObject( [MarshalAs( UnmanagedType.IUnknown )] object pvObject );
			void AddFromArray( [MarshalAs( UnmanagedType.Interface )] IObjectArray poaSource );
			void RemoveObjectAt( uint uiIndex );
			void Clear();
		}

		[ComImport, Guid( "000214F9-0000-0000-C000-000000000046" )]
		[InterfaceType( ComInterfaceType.InterfaceIsIUnknown )]
		interface IShellLinkW
		{
			void GetPath( [MarshalAs( UnmanagedType.LPWStr )] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags );
			void GetIDList( out IntPtr ppidl );
			void SetIDList( IntPtr pidl );
			void GetDescription( [MarshalAs( UnmanagedType.LPWStr )] StringBuilder pszName, int cch );
			void SetDescription( [MarshalAs( UnmanagedType.LPWStr )] string pszName );
			void GetWorkingDirectory( [MarshalAs( UnmanagedType.LPWStr )] StringBuilder pszDir, int cch );
			void SetWorkingDirectory( [MarshalAs( UnmanagedType.LPWStr )] string pszDir );
			void GetArguments( [MarshalAs( UnmanagedType.LPWStr )] StringBuilder pszArgs, int cch );
			void SetArguments( [MarshalAs( UnmanagedType.LPWStr )] string pszArgs );
			void GetHotkey( out short pwHotkey );
			void SetHotkey( short wHotkey );
			void GetShowCmd( out int piShowCmd );
			void SetShowCmd( int iShowCmd );
			void GetIconLocation( [MarshalAs( UnmanagedType.LPWStr )] StringBuilder pszIconPath, int cch, out int piIcon );
			void SetIconLocation( [MarshalAs( UnmanagedType.LPWStr )] string pszIconPath, int iIcon );
			void SetRelativePath( [MarshalAs( UnmanagedType.LPWStr )] string pszPathRel, uint dwReserved );
			void Resolve( IntPtr hwnd, uint fFlags );
			void SetPath( [MarshalAs( UnmanagedType.LPWStr )] string pszFile );
		}

		[ComImport, Guid( "886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99" )]
		[InterfaceType( ComInterfaceType.InterfaceIsIUnknown )]
		interface IPropertyStore
		{
			void GetCount( out uint cProps );
			void GetAt( uint iProp, out PROPERTYKEY pkey );
			void GetValue( ref PROPERTYKEY key, out PROPVARIANT pv );
			void SetValue( ref PROPERTYKEY key, ref PROPVARIANT pv );
			void Commit();
		}

		[ComImport, Guid( "77F10CF0-3DB5-4966-B520-B7C54FD35ED6" )]
		class CDestinationList { }

		[ComImport, Guid( "2D3468C1-36A7-43B6-AC24-D3F02FD9607A" )]
		class CEnumerableObjectCollection { }

		[ComImport, Guid( "00021401-0000-0000-C000-000000000046" )]
		class CShellLink { }
	}
}
