using Sandbox.UI.Construct;

namespace Sandbox.UI;

[Library( "form" )]
public class Form : Panel
{
	protected Panel currentGroup;

	public Form()
	{
		AddClass( "form" );
	}


	public void AddRow( string entryTitle, Panel control )
	{
		var row = AddChild<Field>();

		var title = row.Add.Panel( "label" );
		title.Add.Label( entryTitle );

		var value = row.AddChild<FieldControl>();
		control.Parent = value;
	}

	public void AddHeader( string title, string icon = "category" )
	{
		var row = (currentGroup ?? this).Add.Panel( "field-header" );

		row.Add.Icon( icon ); // todo - get icon from somewhere too
		row.Add.Label( title );
	}

	public void Clear()
	{
		DeleteChildren( true );
	}

	protected override void OnEvent( PanelEvent e )
	{
		//
		// One of our child controls changed, fire a form specific event
		//
		if ( e.Name == "onchange" )
		{
			CreateEvent( "form.changed" );
			return;
		}

		base.OnEvent( e );
	}
}
