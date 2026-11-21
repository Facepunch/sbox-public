using System;

namespace Editor
{
	[AttributeUsage( AttributeTargets.Class )]
	public class EditorAppAttribute : Attribute, Sandbox.ITypeAttribute
	{
		public string Title { get; set; }
		public string Icon { get; set; }
		public string Description { get; set; }
		public Type TargetType { get; set; }


		public EditorAppAttribute( string title, string icon, string description )
		{
			Title = title;
			Icon = icon;
			Description = description;
		}

		public void Open()
		{
			if ( TargetType.IsAssignableTo( typeof( PanelWindow ) ) )
			{
				var window = EditorTypeLibrary.Create<PanelWindow>( TargetType );
				window.Show();
			}
			else if ( TargetType.IsAssignableTo( typeof( Sandbox.UI.Panel ) ) )
			{
				var window = EditorTypeLibrary.Create<PanelWindow>( typeof( PanelWindow ), ["", null] );

				// Catch the exception when the user forgets to add a constructor with `PanelWindow` as its only argument.
				try
				{
					window.Root.AddChild( EditorTypeLibrary.Create<Sandbox.UI.Panel>( TargetType, [window] ) );
				}
				catch ( System.Exception e )
				{
					Log.Error( e, $"Couldn't create EditorApp ({e.Message})" );
					window.Dispose();
					return;
				}

				window.Show();
			}
			else
			{
				var window = EditorTypeLibrary.Create<Widget>( TargetType );
				window.Show();
			}
		}
	}
}
