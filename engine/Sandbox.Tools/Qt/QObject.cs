using System;
using System.Runtime.InteropServices;

namespace Editor
{
	public class QObject : IValid
	{
		internal static Dictionary<Native.QObject, QObject> AllObjects = new();

		internal Native.QObject _object;
		internal Dictionary<GCHandle, CallbackMethod> _handles = new();

		bool IsDestroyed;
		bool IValid.IsValid => IsValid;

		[Hide]
		public bool IsValid => _object.IsValid && !IsDestroyed;

		internal virtual void NativeInit( IntPtr ptr )
		{
			if ( ptr == default )
				throw new System.Exception( "QObject was null!" );

			_object = ptr;

			AllObjects[_object] = this;

			// Get notified when object is destroyed - so we can invalidate our pointers
			WidgetUtil.OnObject_Destroyed( ptr, Callback( NativeShutdown ) );
			EditorEvent.Register( this );
		}

		public virtual void OnDestroyed()
		{

		}

		internal virtual void NativeShutdown()
		{
			EditorEvent.Unregister( this );
			Sandbox.InteropSystem.Free( this );

			AllObjects.Remove( _object );

			_object = default;

			// Can free all the allocated handles now
			// because they won't get called
			foreach ( var handle in _handles )
			{
				handle.Key.Free();
			}

			_handles.Clear();
			_handles = null;

			OnDestroyed();
		}

		internal delegate void CallbackMethod( IntPtr qobj );
		internal IntPtr Callback( Action cb )
		{
			CallbackMethod wrapped = ( IntPtr qobj ) =>
			{
				try
				{
					cb();
				}
				catch ( System.Exception ex )
				{
					try { Log.Error( ex ); }
					catch { }
				}
			};

			_handles.Add( GCHandle.Alloc( wrapped ), wrapped );
			return Marshal.GetFunctionPointerForDelegate( wrapped );
		}

		public void Destroy()
		{
			if ( IsDestroyed )
				return;

			IsDestroyed = true;

			EditorEvent.Unregister( this );

			if ( _object.IsValid )
			{
				OnDestroyingLater();
				_object.deleteMuchLater();
			}
		}

		internal virtual void OnDestroyingLater()
		{

		}

		internal unsafe Native.QObject[] GetChildren()
		{
			if ( !_object.IsValid )
			{
				return Array.Empty<Native.QObject>();
			}

			var c = WidgetUtil.GetChildrenCount( _object );

			if ( c <= 0 )
				return Array.Empty<Native.QObject>();

			var list = new Native.QObject[c];

			fixed ( Native.QObject* ptr = list )
			{
				WidgetUtil.GetChildren( _object, ptr, list.Length );
			}

			return list;
		}

		static internal QObject FindOrCreate( Native.QObject obj )
		{
			if ( AllObjects.TryGetValue( obj, out var handle ) )
				return handle;

			// leafiest first!

			{
				Native.QPushButton ptr = (Native.QPushButton)obj;
				if ( ptr.IsValid ) return new Button( ptr );
			}

			{
				Native.QTabBar ptr = (Native.QTabBar)obj;
				if ( ptr.IsValid ) return new TabBar( ptr );
			}

			{
				Native.QMenuBar ptr = (Native.QMenuBar)obj;
				if ( ptr.IsValid ) return new MenuBar( ptr );
			}

			{
				Native.QWidget ptr = (Native.QWidget)obj;
				if ( ptr.IsValid ) return new Widget( ptr );
			}

			{
				QMimeData ptr = (QMimeData)obj;
				if ( ptr.IsValid ) return new DragData( ptr );
			}

			return null;
		}

		public void SetProperty( string name, bool value )
		{
			_object.setProperty( name, value );
		}

		public void SetProperty( string name, float value )
		{
			_object.setProperty( name, value );
		}

		public void SetProperty( string name, string value )
		{
			_object.setProperty( name, value );
		}

		public Sandbox.Bind.Builder Bind( string targetName, Action onChanged = null )
		{
			var bb = new Sandbox.Bind.Builder
			{
				system = Sandbox.Internal.GlobalToolsNamespace.BindSystem
			};

			return bb.Set( this, targetName, onChanged );
		}

		internal void SetParent( QObject obj )
		{
			_object.setParent( obj._object );
		}
	}
}
