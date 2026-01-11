using System;

namespace Sandbox.UI
{
	/// <summary>
	/// A control that has two panes with a splitter in between. You can drag the splitter to change the size of the two panels.
	/// </summary>
	[Library( "split" )]
	public class SplitContainer : Panel
	{
		/// <summary>
		/// The left, or top panel. Has class "split-left".
		/// </summary>
		public Panel Left { get; protected set; }

		/// <summary>
		/// The left, or bottom panel. Has class "split-right".
		/// </summary>
		public Panel Right { get; protected set; }

		/// <summary>
		/// The splitter control
		/// </summary>
		public Panel Splitter { get; protected set; }

		/// <summary>
		/// The smallest the left section can be as a fraction (0-1). Also controls the largest the right section can be.
		/// </summary>
		public float MinimumFractionLeft = 0.2f;

		/// <summary>
		/// The smallest the right section can be as a fraction (0-1). Also controls the largest the left section can be.
		/// </summary>
		public float MinimumFractionRight = 0.2f;

		/// <summary>
		/// Returns true if splitter is being dragged
		/// </summary>
		public bool IsDragging { get; protected set; }


		bool _vertical;

		/// <summary>
		/// Should this be laid out vertically? If you set this to vertical you should
		/// mentally change Left to Top and Right to Bottom.
		/// </summary>
		public bool Vertical
		{
			get => _vertical;
			set
			{
				if ( _vertical == value ) return;
				_vertical = value;

				SetClass( "vertical", _vertical );
			}
		}


		string _cookie;

		/// <summary>
		/// We can save the position of this splitter in a cookie. To do that set this
		/// (or "cookie" in a template). We'll automatically save and restore from the cookie.
		/// </summary>
		public string FractionCookie
		{
			get => _cookie;
			set
			{
				if ( _cookie == value ) return;
				_cookie = value;

				if ( string.IsNullOrEmpty( _cookie ) )
					return;

				var fr = Game.Cookies.Get( $"splitter.{_cookie}", -1.0f );
				if ( fr >= 0.0f )
				{
					UpdateSplitFraction( fr );
				}
			}
		}

		public SplitContainer()
		{
			AddClass( "splitcontainer" );

			Left = Add.Panel( "split-left" );
			Splitter = Add.Panel( "splitter" );
			Right = Add.Panel( "split-right" );

			Splitter.AddEventListener( "onmousedown", StartDragging );
			Splitter.AddEventListener( "onmouseup", StopDragging );
		}

		/// <summary>
		/// The splitter has been pressed
		/// </summary>
		private void StartDragging( PanelEvent e )
		{
			SetClass( "dragging", true );
			IsDragging = true;
		}

		/// <summary>
		/// The splitter has been released
		/// </summary>
		private void StopDragging( PanelEvent e )
		{
			SetClass( "dragging", false );
			IsDragging = false;
		}

		/// <summary>
		/// If we're dragging then position the split where the mouse is.
		/// </summary>
		protected override void OnMouseMove( MousePanelEvent e )
		{
			if ( IsDragging )
			{
				var local = ScreenPositionToPanelDelta( Mouse.Position );
				UpdateSplitFraction( Vertical ? local.y : local.x );
			}

			base.OnMouseMove( e );
		}

		/// <summary>
		/// Sets the split fraction to this value. Will automatically adjust the value
		/// according to MinimumFraction parameters, and will save the new value to cookie.
		/// </summary>
		public virtual void UpdateSplitFraction( float f )
		{
			f = MathF.Max( f, MinimumFractionLeft );
			if ( (1 - f) < MinimumFractionRight ) f = 1 - MinimumFractionRight;

			if ( Vertical )
			{
				Left.Style.Height = Length.Fraction( f );
				Right.Style.Height = Length.Fraction( 1 - f );
			}
			else
			{
				Left.Style.Width = Length.Fraction( f );
				Right.Style.Width = Length.Fraction( 1 - f );
			}

			Left.Style.Dirty();
			Right.Style.Dirty();

			if ( !string.IsNullOrWhiteSpace( FractionCookie ) )
			{
				Game.Cookies.Set( $"splitter.{FractionCookie}", f );
			}
		}

		/// <summary>
		/// You can create child panels in the template by setting attributes
		/// on them, like slot="left" to make that panel appear in the left panel.
		/// </summary>
		public override void OnTemplateSlot( Html.INode element, string slotName, Panel panel )
		{
			if ( slotName == "left" )
			{
				panel.Parent = Left;
				return;
			}

			if ( slotName == "right" )
			{
				panel.Parent = Right;
				return;
			}

			base.OnTemplateSlot( element, slotName, panel );
		}

		public override void SetProperty( string name, string value )
		{
			if ( name == "direction" )
			{
				Vertical = value == "vertical";
			}

			if ( name == "min-left" )
			{
				MinimumFractionLeft = value.ToFloat();
			}

			if ( name == "min-right" )
			{
				MinimumFractionRight = value.ToFloat();
			}

			if ( name == "default-left" )
			{
				UpdateSplitFraction( value.ToFloat() );
			}

			if ( name == "default-right" )
			{
				UpdateSplitFraction( 1.0f - value.ToFloat() );
			}

			if ( name == "cookie" )
			{
				FractionCookie = value;
			}

			base.SetProperty( name, value );
		}
	}
}
