using System;

namespace Editor
{
	/// <summary>
	/// Like a widget - but is drawn
	/// </summary>
	public class Frame : Widget
	{
		internal Native.QFrame _frame;
		internal Frame() : base( false )
		{

		}

		internal Frame( IntPtr widget ) : base( false )
		{
			NativeInit( widget );
		}

		public Frame( Widget parent ) : base( false )
		{
			Sandbox.InteropSystem.Alloc( this );
			var widget = CFrame.CreateFrame( parent?._widget ?? default, this );
			NativeInit( widget );
		}

		internal override void NativeInit( IntPtr ptr )
		{
			_frame = ptr;

			base.NativeInit( ptr );
		}

		internal virtual void NativeDestroying()
		{
		}

		internal override void NativeShutdown()
		{
			_frame = default;

			base.NativeShutdown();
		}
	}
}
