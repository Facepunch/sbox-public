using Sandbox.UI;
using System;

namespace Editor
{
	public abstract class Layout : QObject
	{
		internal Native.QLayout _layout;

		internal Layout( IntPtr ptr )
		{
			NativeInit( ptr );
			ThreadSafe.AssertIsMainThread();
		}

		internal override void NativeInit( IntPtr ptr )
		{
			_layout = ptr;
			Spacing = 0;
			Margin = 0;

			base.NativeInit( ptr );
			ThreadSafe.AssertIsMainThread();
		}

		internal override void NativeShutdown()
		{
			_layout = default;

			base.NativeShutdown();
			ThreadSafe.AssertIsMainThread();
		}

		/// <summary>
		/// The amount of space between items
		/// </summary>
		public float Spacing
		{
			get => _layout.spacing();
			set => _layout.setSpacing( (int)value );
		}


		SizeConstraint _sc;

		/// <summary>
		/// How the layout should resize the owning widget
		/// </summary>
		public SizeConstraint SizeConstraint
		{
			get => _sc;
			set
			{
				_sc = value;
				_layout.setSizeConstraint( value );
			}
		}

		/// <summary>
		/// An enabled layout adjusts dynamically to changes; a disabled layout acts as if it did not exist.
		/// </summary>
		public bool Enabled
		{
			get => _layout.isEnabled();
			set => _layout.setEnabled( value );
		}

		/// <summary>
		/// An enabled layout adjusts dynamically to changes; a disabled layout acts as if it did not exist.
		/// </summary>
		public TextFlag Alignment
		{
			get => (TextFlag)_layout.alignment();
			set => _layout.setAlignment( (int)value );
		}

		/// <summary>
		/// The rect of this layout including margins
		/// </summary>
		public Rect OuterRect => _layout.geometry().Rect;

		/// <summary>
		/// The rect of this layout excluding margins
		/// </summary>
		public Rect InnerRect => _layout.contentsRect().Rect;

		Margin _contentMargin;

		/// <summary>
		/// The amount of space to leave free around the outside of the layout
		/// </summary>
		public Margin Margin
		{
			get => _contentMargin;
			set
			{
				_contentMargin = value;
				_layout.setContentsMargins( (int)_contentMargin.Left, (int)_contentMargin.Top, (int)_contentMargin.Right, (int)_contentMargin.Bottom );
			}
		}

		/// <summary>
		/// Remove all widgets from this layout, without deleting them outright.
		/// </summary>
		/// <param name="deleteWidgets">Also delete all the widgets.</param>
		public void Clear( bool deleteWidgets )
		{
			_layout.clear( deleteWidgets );
		}

		public abstract Layout Add( Layout layout );

		public virtual Layout Add( Layout layout, int stretch )
		{
			Assert.IsValid( layout );
			return Add( layout );
		}

		public virtual T AddLayout<T>( T layout, int stretch = 0 ) where T : Layout
		{
			Assert.IsValid( layout );
			Add( layout );

			return layout;
		}

		public virtual T Add<T>( T widget ) where T : Widget
		{
			Assert.IsValid( widget );

			_layout.addWidget( widget._widget );
			return widget;
		}

		public virtual T Add<T>( T widget, int stretch ) where T : Widget
		{
			return Add( widget );
		}


		//
		// Types
		//

		public static Layout Row( bool reversed = false )
		{
			return new BoxLayout( reversed ? BoxLayout.Direction.RightToLeft : BoxLayout.Direction.LeftToRight, default );
		}

		public static Layout Column( bool reversed = false )
		{
			return new BoxLayout( reversed ? BoxLayout.Direction.BottomToTop : BoxLayout.Direction.TopToBottom, default );
		}

		public static GridLayout Grid() => new GridLayout();

		public Layout AddRow( int stretch = 0, bool reversed = false ) => Add( Row( reversed ), stretch );
		public Layout AddColumn( int stretch = 0, bool reversed = false ) => Add( Column( reversed ), stretch );

		/// <summary>
		/// Add a spacing item
		/// </summary>
		public virtual void AddSpacingCell( float size )
		{
			throw new NotImplementedException();
		}

		/// <summary>
		/// Add a stretch item
		/// </summary>
		public virtual void AddStretchCell( int stretch = 0 )
		{
			throw new NotImplementedException();
		}

		/// <summary>
		/// Adds a 1 pixel line
		/// </summary>
		public Separator AddSeparator( bool light = false )
		{
			var separator = Add( new Separator( 1 ) { Color = light ? Color.White.WithAlpha( 0.1f ) : Color.Black.WithAlpha( 0.3f ) } );
			return separator;
		}

		/// <summary>
		/// Adds a line
		/// </summary>
		public Separator AddSeparator( float size, Color color )
		{
			var separator = Add( new Separator( size ) { Color = color } );
			return separator;
		}

	}

}


