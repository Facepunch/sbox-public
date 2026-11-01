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
			else
			{
				var window = EditorTypeLibrary.Create<Widget>( TargetType );
				window.Show();
			}
		}
	}
}
