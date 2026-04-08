using Sandbox;
using System;

namespace Editor
{
	public class BaseScrollWidget : Frame
	{
		internal Native.QAbstractScrollArea _scrollarea;
		internal CAbstractScrollArea _cscrollarea;

		/// <summary>
		/// The vertical scroll bar.
		/// </summary>
		public ScrollBar VerticalScrollbar { get; init; }

		/// <summary>
		/// The horizontal scroll bar.
		/// </summary>
		public ScrollBar HorizontalScrollbar { get; init; }

		public bool SmoothScrolling { get; set; } = true;

		Widget viewport;

		public BaseScrollWidget( Widget parent = null )
		{
			Sandbox.InteropSystem.Alloc( this );
			_cscrollarea = CAbstractScrollArea.Create( parent?._widget ?? default, this );
			NativeInit( _cscrollarea );

			VerticalScrollbar = new ScrollBar( _scrollarea.verticalScrollBar() );
			HorizontalScrollbar = new ScrollBar( _scrollarea.horizontalScrollBar() );
			MouseTracking = true;
		}

		internal override void NativeInit( IntPtr ptr )
		{
			_scrollarea = ptr;

			viewport = new Widget( _scrollarea.viewport() );

			base.NativeInit( ptr );
		}
		internal override void NativeShutdown()
		{
			base.NativeShutdown();

			_scrollarea = default;
			_cscrollarea = default;
		}

		/// <summary>
		/// <see cref="HorizontalScrollbar"/> mode.
		/// </summary>
		public ScrollbarMode HorizontalScrollbarMode
		{
			get => _scrollarea.horizontalScrollBarPolicy();
			set => _scrollarea.setHorizontalScrollBarPolicy( value );
		}

		/// <summary>
		/// <see cref="VerticalScrollbar"/> mode.
		/// </summary>
		public ScrollbarMode VerticalScrollbarMode
		{
			get => _scrollarea.verticalScrollBarPolicy();
			set => _scrollarea.setVerticalScrollBarPolicy( value );
		}

		public override void Update()
		{
			if ( !IsValid ) return;

			viewport.Update();
		}

		internal void InternalOnScrollChanged() => OnScrollChanged();

		/// <summary>
		/// Called when the scroll position has changed.
		/// </summary>
		protected virtual void OnScrollChanged()
		{

		}

		//
		// smooth scrolling
		//

		/// <summary>
		/// The smooth scrolling wants to move by this amount
		/// </summary>
		public float SmoothScrollTarget { get; set; }

		/// <summary>
		/// We save off the scroll value to a float during smooth scroll so it can
		/// keep hold of fractions, that way we don't get chunky rounding errors.
		/// </summary>
		float SmoothValue;

		protected override void OnMouseWheel( WheelEvent e )
		{
			if ( !SmoothScrolling )
			{
				base.OnMouseWheel( e );
				return;
			}

			e.Accept();

			if ( e.Delta < 0 ) SmoothScrollTarget += VerticalScrollbar.SingleStep;
			else SmoothScrollTarget -= VerticalScrollbar.SingleStep;
		}

		protected override void OnKeyPress( KeyEvent e )
		{
			if ( e.Key == KeyCode.PageDown && SmoothScrolling )
			{
				SmoothScrollTarget += VerticalScrollbar.PageStep;
				e.Accepted = true;
				return;
			}

			if ( e.Key == KeyCode.PageUp && SmoothScrolling )
			{
				SmoothScrollTarget -= VerticalScrollbar.PageStep;
				e.Accepted = true;
				return;
			}

			if ( e.Key == KeyCode.Home && SmoothScrolling )
			{
				SmoothScrollTarget += (VerticalScrollbar.Minimum - VerticalScrollbar.Value);
				e.Accepted = true;
				return;
			}

			if ( e.Key == KeyCode.End && SmoothScrolling )
			{
				SmoothScrollTarget += (VerticalScrollbar.Maximum - VerticalScrollbar.Value);
				e.Accepted = true;
				return;
			}


			// todo arrows

			base.OnKeyPress( e );
		}

		float smoothScrollVelocity;
		float lastScrollbarValue = 0.0f;

		[EditorEvent.Frame]
		public virtual void ScrollingFrame()
		{
			const float scrollTime = 0.2f;

			//
			// Scrollbar value changed, obey the scrollbar
			//
			if ( lastScrollbarValue != VerticalScrollbar.Value )
			{
				lastScrollbarValue = VerticalScrollbar.Value;
				SmoothScrollTarget = 0;
				SmoothValue = lastScrollbarValue;
				OnScrollChanged();
				return;
			}

			if ( SmoothScrollTarget.AlmostEqual( 0.0f, 0.01f ) )
			{
				//
				// Sync this in case the user drags the scrollbar manually
				//
				SmoothValue = VerticalScrollbar.Value;
				return;
			}

			//
			// Don't limit the framerate for the next 10 frames
			//
			g_pToolFramework2.SetWantsFullFrameRate( 10 );

			var dest = MathX.SmoothDamp( SmoothScrollTarget, 0, ref smoothScrollVelocity, scrollTime, RealTime.Delta );

			var difference = SmoothScrollTarget - dest;
			SmoothScrollTarget = dest;

			SmoothValue += difference;
			if ( SmoothValue < VerticalScrollbar.Minimum ) SmoothValue = VerticalScrollbar.Minimum;
			if ( SmoothValue > VerticalScrollbar.Maximum ) SmoothValue = VerticalScrollbar.Maximum;

			VerticalScrollbar.Value = (int)SmoothValue;
			lastScrollbarValue = VerticalScrollbar.Value;
		}
	}
}
