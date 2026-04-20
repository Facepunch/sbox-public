namespace Editor.MeshEditor;

partial class BridgeTool
{
	public static int NumSteps { get; set; } = 4;
	public static int Twist { get; set; } = 0;
	public static PolygonMesh.BridgeUVMode UVMode { get; set; } = PolygonMesh.BridgeUVMode.Auto;
	public static float RepeatsU { get; set; } = 1.0f;
	public static float RepeatsV { get; set; } = 1.0f;

	public override Widget CreateToolSidebar()
	{
		return new BridgeToolWidget( this );
	}

	public class BridgeToolWidget : ToolSidebarWidget
	{
		private readonly BridgeTool _tool;

		private struct BridgeProperties
		{
			[Title( "Steps" ), Range( 1, 128 ), Step( 1 ), WideMode]
			public readonly int Steps { get => NumSteps; set => NumSteps = value; }

			[Title( "Twist" ), Range( -100, 100, true, false ), Step( 1 ), WideMode]
			public readonly int TwistAmount { get => Twist; set => Twist = value; }

			[Title( "UV Mode" ), WideMode]
			public readonly PolygonMesh.BridgeUVMode UVs { get => UVMode; set => UVMode = value; }

			[Title( "Repeats U" ), Range( 0.01f, 100.0f, true, false ), Step( 0.0625f ), WideMode]
			public readonly float U { get => RepeatsU; set => RepeatsU = value; }

			[Title( "Repeats V" ), Range( 0.01f, 100.0f, true, false ), Step( 0.0625f ), WideMode]
			public readonly float V { get => RepeatsV; set => RepeatsV = value; }
		}

		[InlineEditor( Label = false )]
		readonly BridgeProperties _bridgeProperties = new();

		public BridgeToolWidget( BridgeTool tool ) : base()
		{
			_tool = tool;

			AddTitle( "Bridge Tool", "device_hub" );

			var group = AddGroup( "Properties" );

			var row = group.AddRow();
			row.Spacing = 8;

			var sheet = new ControlSheet();
			var control = sheet.AddRow( this.GetSerialized().GetProperty( nameof( _bridgeProperties ) ) );
			control.OnChildValuesChanged += _ => UpdateMesh();
			row.Add( sheet );

			row = group.AddRow();
			row.Spacing = 4;

			var apply = new Button( "Apply", "done" );
			apply.Clicked = Apply;
			row.Add( apply );

			var cancel = new Button( "Cancel", "close" );
			cancel.Clicked = Cancel;
			row.Add( cancel );

			Layout.AddStretchCell();

			UpdateMesh();
		}

		void UpdateMesh()
		{
			_tool.UpdateBridge( NumSteps, Twist, UVMode, RepeatsU, RepeatsV );
		}

		private void Cancel()
		{
			_tool.Cancel();
		}

		private void Apply()
		{
			_tool.Apply();
		}

		[Shortcut( "mesh.bridge-increase", "]", typeof( SceneViewWidget ) )]
		private void IncreaseSteps()
		{
			NumSteps = Math.Min( 128, NumSteps + 1 );
			UpdateMesh();
		}

		[Shortcut( "mesh.bridge-decrease", "[", typeof( SceneViewWidget ) )]
		private void DecreaseSteps()
		{
			NumSteps = Math.Max( 1, NumSteps - 1 );
			UpdateMesh();
		}

		[Shortcut( "mesh.bridge-apply", "enter", ShortcutType.Application )]
		private void ApplyShortcut()
		{
			Apply();
		}

		[Shortcut( "mesh.bridge-cancel", "ESC", ShortcutType.Application )]
		private void CancelShortcut()
		{
			Cancel();
		}
	}
}
