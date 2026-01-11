namespace Sandbox.UI
{
	/// <summary>
	/// A panel containing an icon, typically a <a href="https://fonts.google.com/icons">material icon</a>.
	/// </summary>
	[Library( "IconPanel" ), Alias( "icon", "i" ),]
	public class IconPanel : Label
	{
		public IconPanel()
		{
			AddClass( "iconpanel" );
		}

		public override string Text
		{
			get => base.Text;
			set
			{
				if ( value?.StartsWith( "https://" ) ?? false )
				{
					Log.Info( value );
					Style.SetBackgroundImage( value );
					base.Text = "";
					return;
				}

				Style.BackgroundImage = null;
				base.Text = value;
			}
		}

		public IconPanel( string icon, string classes = null ) : base()
		{
			Text = icon;
			AddClass( classes );
		}
	}

	namespace Construct
	{
		public static class IconPanelConstructor
		{
			/// <summary>
			/// Create and return an icon (panel) with given icon and optionally given CSS classes.
			/// </summary>
			public static IconPanel Icon( this PanelCreator self, string icon, string classes = null )
			{
				var control = self.panel.AddChild<IconPanel>();

				if ( icon != null )
					control.Text = icon;

				if ( classes != null )
					control.AddClass( classes );

				return control;
			}
		}
	}

}
