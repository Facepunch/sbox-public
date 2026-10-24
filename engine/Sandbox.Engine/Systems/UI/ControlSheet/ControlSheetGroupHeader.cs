using Microsoft.AspNetCore.Components;
using Sandbox.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static Sandbox.Internal.GlobalGameNamespace;


namespace Sandbox.UI;

/// <summary>
/// A group's heading, laid out like the editor's: the title in the label column, a divider
/// across the control column, and a fold button at the end. Quieter while folded.
/// </summary>
[StyleSheet.Inline( "controlsheetgroupheader", Styles )]
public class ControlSheetGroupHeader : Panel
{
	const string Styles = """
		ControlSheetGroupHeader
		{
			min-height: 32px;
			padding: 0px 4px 0px 6px;
			align-items: center;
			color: #fff;
			font-size: 14px;
			font-weight: 600;
			text-shadow: 1px 1px 1px #0004;

			&.hidden
			{
				display: none;
			}

			&.folded
			{
				color: #fff9;
			}

			&.foldable, &.has-toggle
			{
				cursor: pointer;
			}

			&.foldable:hover, &.has-toggle:hover
			{
				color: #fff;
			}

			// Title and icon fill the label column, so every divider starts where the controls do
			> .lead
			{
				flex-shrink: 0;
				width: 144px;
				padding-right: 12px;
				gap: 6px;
				align-items: center;
				overflow: hidden;
			}

			> .lead > .title
			{
				flex-shrink: 1;
				min-width: 0px;
				white-space: nowrap;
				overflow: hidden;
				text-overflow: ellipsis;
			}

			> .lead > .icon
			{
				flex-shrink: 0;

				&.hidden
				{
					display: none;
				}
			}

			> .divider
			{
				flex-grow: 1;
				height: 1px;
				margin-right: 8px;
				background-color: #ffffff1a;
			}

			&.folded > .divider
			{
				background-color: #ffffff0d;
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

				&.hidden
				{
					display: none;
				}
			}

			&.has-toggle
			{
				> .lead
				{
					// The checkbox sits in front, inside the same column
					width: 116px;
				}

				&:before
				{
					content: ' ';
					flex-shrink: 0;
					width: 22px;
					height: 22px;
					margin-right: 6px;
					background-color: #000a;
					align-items: center;
					justify-content: center;
					text-align: center;
					border-radius: 5px;
					border: 1px solid #555;
				}

				&:hover:before
				{
					border-color: #888;
				}

				&.checked:before
				{
					content: '✓';
					font-weight: bold;
					color: #08f;
					border-color: #08f;
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
		var lead = AddChild<Panel>( "lead" );
		iconLabel = lead.AddChild<Label>( "icon" );
		textLabel = lead.AddChild<Label>( "title" );
		AddChild<Panel>( "divider" );
		foldIcon = AddChild( new IconPanel( "expand_more", "fold" ) );
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
		if ( foldable ) foldIcon.Text = Folded ? "chevron_right" : "expand_more";

		// A switched-off group reads as folded too - its body is hidden the same way
		SetClass( "folded", ToggleProperty is not null ? ToggleProperty.As.Bool == false : foldable && Folded );
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
