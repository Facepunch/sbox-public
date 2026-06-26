namespace Editor.MeshEditor;

partial class InsetTool
{
	public static float InsetAmount { get; set; } = 8.0f;
	public static int InsetSteps { get; set; } = 0;

	public override Widget CreateToolSidebar()
	{
		return new InsetToolWidget( this );
	}

	public class InsetToolWidget : ToolSidebarWidget
	{
		private readonly InsetTool _tool;

		private struct InsetProperties
		{
			[Title( "Amount" ), Range( -256.0f, 256.0f, true, false ), Step( 2.0f ), WideMode]
			public readonly float Amount { get => InsetAmount; set => InsetAmount = value; }

			[Title( "Steps" ), Range( 0, 32 ), Step( 1 ), WideMode]
			public readonly int Steps { get => InsetSteps; set => InsetSteps = value; }
		}

		[InlineEditor( Label = false )]
		readonly InsetProperties _insetProperties = new();

		public InsetToolWidget( InsetTool tool ) : base()
		{
			_tool = tool;

			AddTitle( "Inset Tool", "filter_center_focus" );

			{
				var group = AddGroup( "Properties" );
				var row = group.AddRow();
				row.Spacing = 8;

				var sheet = new ControlSheet();
				var control = sheet.AddRow( this.GetSerialized().GetProperty( nameof( _insetProperties ) ) );
				control.OnChildValuesChanged += _ => UpdateMesh();
				row.Add( sheet );

				row = group.AddRow();
				row.Spacing = 4;

				var apply = new Button( "Apply", "done" );
				apply.Clicked = Apply;
				apply.ToolTip = "[Apply " + EditorShortcuts.GetKeys( "mesh.inset-apply" ) + "]";
				row.Add( apply );

				var cancel = new Button( "Cancel", "close" );
				cancel.Clicked = Cancel;
				cancel.ToolTip = "[Cancel " + EditorShortcuts.GetKeys( "mesh.inset-cancel" ) + "]";
				row.Add( cancel );
			}

			Layout.AddStretchCell();

			UpdateMesh();
		}

		void UpdateMesh()
		{
			_tool.UpdateInset( InsetAmount, InsetSteps );
		}

		[Shortcut( "mesh.inset-apply", "enter", ShortcutType.Application )]
		void Apply()
		{
			_tool.Apply();
		}

		[Shortcut( "mesh.inset-cancel", "ESC", ShortcutType.Application )]
		void Cancel()
		{
			_tool.Cancel();
		}

		[Shortcut( "mesh.inset-increase", "]", typeof( SceneViewWidget ) )]
		void IncreaseAmount()
		{
			InsetAmount = MathF.Min( 256.0f, InsetAmount + 1.0f );
			UpdateMesh();
		}

		[Shortcut( "mesh.inset-decrease", "[", typeof( SceneViewWidget ) )]
		void DecreaseAmount()
		{
			InsetAmount = MathF.Max( -256.0f, InsetAmount - 1.0f );
			UpdateMesh();
		}
	}
}
