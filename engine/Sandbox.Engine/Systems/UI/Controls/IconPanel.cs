using Microsoft.AspNetCore.Components;
using Sandbox.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static Sandbox.Internal.GlobalGameNamespace;

namespace Sandbox.UI
{
	/// <summary>
	/// A panel containing an icon, typically a <a href="https://fonts.google.com/icons">material icon</a>.
	/// </summary>
	[Library( "IconPanel" ), Alias( "icon", "i" )]
	[StyleSheet.Inline( "iconpanel", Styles )]
	public class IconPanel : Label
	{
		const string Styles = """
			IconPanel
			{
				font-family: Material Icons;
			}
			""";

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
