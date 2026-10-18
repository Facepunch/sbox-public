using Microsoft.AspNetCore.Components;
using Sandbox.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static Sandbox.Internal.GlobalGameNamespace;


namespace Sandbox.UI;

[StyleSheet.Inline( "controlsheetgroupheader", Styles )]

public class ControlSheetGroupHeader : Panel
{
	const string Styles = """
		ControlSheetGroupHeader
		{
			font-size: 1.33rem;
			color: red;
			gap: 6px;
			align-items: center;

			&.hidden
			{
				display: none;
			}

			> .title
			{
				font-weight: 600;
			}

			> .icon.hidden, > .fold.hidden
			{
				display: none;
			}

			> .fold
			{
				flex-shrink: 0;
				width: 19px;
				height: 19px;
				border-radius: 10px;
				background-color: #0006;
				font-size: 14px;
				align-items: center;
				justify-content: center;
				text-align: center;
			}

			&.foldable
			{
				cursor: pointer;
			}

			&.has-toggle
			{
				cursor: pointer;
				opacity: 0.8;

				&:before
				{
					content: ' ';
					width: 22px;
					height: 22px;
					background-color: #000a;
					align-items: center;
					justify-content: center;
					text-align: center;
					border-radius: 5px;
					border: 1px solid #555;
				}

				&:hover
				{
					opacity: 1;

					&:before
					{
						border-color: #888;
					}
				}

				&.checked
				{
					> .title
					{
						color: white;
					}

					&:before
					{
						content: '✓';
						font-weight: bold;
						color: #08f;
						border-color: #08f;
					}
				}
			}
		}
		""";

	public string Title
	{
		get;
		set
		{
			field = value;
			textLabel.Text = field;
		}
	}

	public string Icon
	{
		get;
		set
		{
			field = value;
			iconLabel.Text = field;
		}
	}

	public SerializedProperty ToggleProperty
	{
		get;
		set
		{
			field = value;

			SetClass( "has-toggle", field != null );
		}
	}

	/// <summary>
	/// Shows the fold button; clicking the header calls <see cref="OnFold"/>.
	/// </summary>
	internal bool Foldable { get; set; }
	internal bool Folded { get; set; }
	internal Action OnFold { get; set; }

	Label foldIcon;
	Label iconLabel;
	Label textLabel;

	public ControlSheetGroupHeader()
	{
		foldIcon = AddChild( new IconPanel( "remove", "fold" ) );
		iconLabel = AddChild<Label>( "icon" );
		textLabel = AddChild<Label>( "title" );
	}

	public override void Tick()
	{
		base.Tick();

		SetClass( "checked", ToggleProperty?.As.Bool == true );
		SetClass( "hidden", string.IsNullOrWhiteSpace( Title ) && ToggleProperty == null );

		// An empty icon would still indent the title
		iconLabel.SetClass( "hidden", string.IsNullOrWhiteSpace( Icon ) );

		var foldable = Foldable && ToggleProperty == null;
		SetClass( "foldable", foldable );
		foldIcon.SetClass( "hidden", !foldable );
		if ( foldable ) foldIcon.Text = Folded ? "add" : "remove";
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );

		if ( ToggleProperty is not null )
			ToggleProperty.As.Bool = !ToggleProperty.As.Bool;
		else if ( Foldable )
			OnFold?.Invoke();
	}
}
