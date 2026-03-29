using Sandbox.Engine;
using Sandbox.Internal;
using System;

namespace Editor
{
	/// <summary>
	/// Used to tell the user what kind of action will happen during a drag and drop event on mouse release.
	/// In Windows, these actions will also display text near cursor to let the user know what will happen if they release their mouse button.
	/// </summary>
	public enum DropAction
	{
		/// <summary>
		/// The data will be copied.
		/// </summary>
		Copy = 0x1,

		/// <summary>
		/// The data will be moved.
		/// </summary>
		Move = 0x2,

		/// <summary>
		/// The data will be linked.
		/// </summary>
		Link = 0x4,

		/// <summary>
		/// Ignore this drop action.
		/// </summary>
		Ignore = 0x0
	}

	public class Drag : QObject
	{
		internal QDrag _drag;

		public DragData Data { get; private set; }


		public Drag( QObject parent )
		{
			NativeInit( QDrag.Create( parent._object ) );

			Data = new DragData();
			_drag.setMimeData( Data._data );
		}

		public Drag( GraphicsMouseEvent e )
		{
			NativeInit( QDrag.Create( e.ptr.widget() ) );

			Data = new DragData();
			_drag.setMimeData( Data._data );
		}

		internal override void NativeInit( IntPtr ptr )
		{
			_drag = ptr;

			base.NativeInit( ptr );
		}

		internal override void NativeShutdown()
		{
			_drag = default;

			base.NativeShutdown();

		}

		public void SetImage( Pixmap image )
		{
			_drag.setPixmap( image.ptr );
		}

		public DropAction ExecuteBlocking()
		{
			try
			{
				DragData.Current = Data;
				return _drag.exec();
			}
			finally
			{
				DragData.Current = null;
			}
		}

		public void Execute()
		{
			BlockingLoopPumper.PendingFunction += () =>
			{
				try
				{
					DragData.Current = Data;
					if ( _drag.IsValid )
					{
						_drag.exec();
					}
				}
				finally
				{
					DragData.Current = null;
				}
			};
		}
	}

	/// <summary>
	/// Contains drag and drop data for tool widgets. See <see cref="Widget.DragEvent"/>.
	/// </summary>
	public partial class DragData : QObject
	{
		internal static DragData Current { get; set; }
		internal QMimeData _data;

		public DragData()
		{
			NativeInit( QMimeData.Create() );
		}

		internal DragData( QMimeData mimedata )
		{
			NativeInit( mimedata );
		}

		internal override void NativeInit( IntPtr ptr )
		{
			_data = ptr;

			base.NativeInit( ptr );
		}

		internal override void NativeShutdown()
		{
			_data = default;

			base.NativeShutdown();
		}

		/// <summary>
		/// An object that can be used to pass drag and drop data
		/// </summary>
		public object Object { get; set; }

		/// <summary>
		/// Text data of the drag and drop event.
		/// </summary>
		public string Text
		{
			get => _data.text();
			set => _data.setText( value );
		}

		/// <summary>
		/// HTML data of the drag and drop event, if any.
		/// </summary>
		public string Html
		{
			get => _data.html();
			set => _data.setHtml( value );
		}

		/// <summary>
		/// URL data of the drag and drop event, if any.
		/// </summary>
		public Uri Url
		{
			get
			{
				var url = _data.url();
				if ( string.IsNullOrEmpty( url ) ) return default;

				if ( url.StartsWith( "file:///" ) )
				{
					// hash is a valid char in filenames
					// encode it so it's not misinterpreted  as a fragment, which breaks the path
					url = url.Replace( "#", "%23" );
				}

				if ( Uri.TryCreate( url, new UriCreationOptions(), out var uri ) )
				{
					return uri;
				}

				return default;
			}
			set => _data.setUrl( value.ToString() );
		}

		/// <summary>
		/// Whether the drag data has at least 1 file or folder.
		/// </summary>
		public bool HasFileOrFolder => Text.StartsWith( "file:///" ) || (Url != null && Url.IsFile);

		/// <summary>
		/// The first file or folder in the drag data.
		/// </summary>
		public string FileOrFolder
		{
			get
			{
				if ( Url?.IsFile ?? false )
				{
					return Url.LocalPath;
				}

				var pre = "file:///";
				var f = Text;

				if ( f.Length < pre.Length + 1 ) return null;
				if ( !f.StartsWith( pre ) ) return null;

				return f[pre.Length..];
			}
		}

		/// <summary>
		/// All files and folders in the drag data.
		/// </summary>
		public string[] Files
		{
			get
			{
				var files = Text.Split( "\n" );

				// No spaces means only 1 file, so just use FileOrFolder.
				if ( files.Length < 2 )
				{
					return new string[] { FileOrFolder };
				}

				var pre = "file:///";
				List<string> output = new();

				foreach ( var file in files )
				{
					// Windows file explorer drags..
					if ( file.Length > pre.Length + 1 && file.StartsWith( pre ) )
					{
						output.Add( file[pre.Length..] );
					}

					// If URL is a file, and the text has a dot, it's probably a file
					if ( HasFileOrFolder && file.IndexOf( "." ) != -1 )
					{
						output.Add( file );
					}
				}

				return output.ToArray();
			}
		}

		/// <summary>
		/// Helper for finding instances of type <typeparamref name="T"/> in <see cref="Object"/>.
		/// Will find matches if <see cref="Object"/> is of type <typeparamref name="T"/>, is
		/// an <see cref="IEnumerable"/> with <typeparamref name="T"/> items, or a <see cref="SerializedObject"/>
		/// with <typeparamref name="T"/> targets.
		/// </summary>
		public IEnumerable<T> OfType<T>()
		{
			return Object switch
			{
				SerializedObject { Targets: { } targets } => targets.OfType<T>(),
				IEnumerable enumerable => typeof( T ) == typeof( object ) ? enumerable.Cast<T>() : enumerable.OfType<T>(),
				T value => new[] { value },
				_ => Enumerable.Empty<T>()
			};
		}

		/// <summary>
		/// Helper for finding instances of type <paramref name="type"/> in <see cref="Object"/>.
		/// Will find matches if <see cref="Object"/> is of type <paramref name="type"/>, is
		/// an <see cref="IEnumerable"/> with <paramref name="type"/> items, or a <see cref="SerializedObject"/>
		/// with <paramref name="type"/> targets.
		/// </summary>
		public IEnumerable<object> OfType( Type type )
		{
			return Object switch
			{
				SerializedObject { Targets: { } targets } => targets.Where( type.IsInstanceOfType ),
				IEnumerable enumerable => type == typeof( object ) ? enumerable.Cast<object>() : enumerable.OfType<object>().Where( type.IsInstanceOfType ),
				_ when type.IsInstanceOfType( Object ) => new[] { Object },
				_ => Enumerable.Empty<object>()
			};
		}
	}
}
