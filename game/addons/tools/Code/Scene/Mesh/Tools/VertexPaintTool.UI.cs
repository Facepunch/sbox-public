using System.Runtime.InteropServices;

namespace Editor.MeshEditor;

partial class VertexPaintTool
{
	public override Widget CreateToolSidebar()
	{
		return new VertexPaintToolWidget( this );
	}

	public class VertexPaintToolWidget : ToolSidebarWidget
	{
		readonly Widget _blendRow;
		readonly ControlSheetRow _paintRow;

		public VertexPaintToolWidget( VertexPaintTool tool ) : base()
		{
			AddTitle( "Vertex Paint Tool", "brush" );

			var so = tool.GetSerialized();

			{
				var group = AddGroup( "Paint On" );
				group.Add( ControlSheetRow.Create( so.GetProperty( nameof( tool.PaintOnSelected ) ) ) );
				group.Add( ControlSheetRow.Create( so.GetProperty( nameof( tool.LimitToActiveMaterial ) ) ) );
				group.Add( ControlSheetRow.Create( so.GetProperty( nameof( tool.PaintBackfacing ) ) ) );
			}
			{
				var group = AddGroup( "Painting" );

				var modeProp = so.GetProperty( nameof( tool.Mode ) );
				var modeRow = ControlSheetRow.Create( modeProp );
				group.Add( modeRow );

				_blendRow = new Widget( this );
				_blendRow.Layout = Layout.Row();
				_blendRow.Layout.Margin = 4;
				_blendRow.Layout.Spacing = 4;

				var material = tool.Tool.ActiveMaterial;
				var blendCount = material.IsValid() ? material.GetFeature( "F_MULTIBLEND" ) : 0;

				var masks = new[]
				{
					(BlendMask.A, new Vector4( 0, 0, 0, 1 ), "0" ),
					(BlendMask.R, new Vector4( 1, 0, 0, 0 ), "1" ),
					(BlendMask.G, new Vector4( 0, 1, 0, 0 ), "2" ),
					(BlendMask.B, new Vector4( 0, 0, 1, 0 ), "3" ),
				};

				var blendWidgets = new List<BlendWidget>();

				if ( blendCount == 0 )
				{
					var label = new Label( "Active material does not support vertex painting." );
					label.WordWrap = true;
					_blendRow.Layout.Add( label );
				}
				else
				{
					var size = blendCount switch
					{
						1 => 96,
						2 => 64,
						3 => 52,
						_ => 42
					};
					for ( int i = 0; i < blendCount + 1 && i < masks.Length; i++ )
					{
						var (maskId, maskVec, layerLabel) = masks[i];
						var pixmap = CreateBlendPixmap( material, 42, maskVec );

						var w = new BlendWidget
						{
							FixedWidth = size,
							FixedHeight = size + BlendWidget.LabelHeight,
							Pixmap = CreateBlendPixmap( tool.Tool.ActiveMaterial, size, maskVec ),
							Selected = tool.ActiveBlendMask == maskId,
							Label = layerLabel
						};

						w.OnClicked = () =>
						{
							tool.ActiveBlendMask = maskId;

							foreach ( var bw in blendWidgets )
								bw.Selected = false;

							w.Selected = true;

							Update();
						};

						blendWidgets.Add( w );
						_blendRow.Layout.Add( w );
					}
				}

				_paintRow = ControlSheetRow.Create( so.GetProperty( nameof( tool.Color ) ) );

				group.Add( ControlSheetRow.Create( so.GetProperty( nameof( tool.Radius ) ) ) );
				group.Add( ControlSheetRow.Create( so.GetProperty( nameof( tool.Strength ) ) ) );
				group.Add( _blendRow );
				group.Add( _paintRow );

				modeProp.OnChanged += ( e ) => UpdateModeVisibility( tool.Mode );
			}
			{
				var group = AddGroup( "Visualization" );
				group.Add( ControlSheetRow.Create( so.GetProperty( nameof( tool.ShowVerts ) ) ) );
			}

			Layout.AddStretchCell();

			UpdateModeVisibility( tool.Mode );
		}

		void UpdateModeVisibility( PaintMode mode )
		{
			_blendRow.Visible = mode == PaintMode.Blend;
			_paintRow.Visible = mode == PaintMode.Color;
		}

		class BlendWidget : Widget
		{
			public Pixmap Pixmap;
			public bool Selected;
			public string Label;
			public Action OnClicked;

			public const float LabelHeight = 20f;
			const float Rounding = 4f;

			public BlendWidget()
			{
				MouseTracking = true;
				Cursor = CursorShape.Finger;
			}

			protected override void OnMousePress( MouseEvent e )
			{
				OnClicked?.Invoke();
				e.Accepted = true;
			}

			protected override void OnPaint()
			{
				Paint.Antialiasing = true;

				var rect = LocalRect;
				var previewRect = new Rect( rect.Left, rect.Top, rect.Width, rect.Width );
				var labelRect = new Rect( rect.Left, previewRect.Bottom, rect.Width, LabelHeight );

				Paint.ClearBrush();
				Paint.ClearPen();

				Paint.Draw( previewRect.Shrink( 2 ), Pixmap );

				Paint.ClearBrush();
				if ( Selected )
				{
					Paint.SetPen( Theme.Primary, 2 );
					Paint.DrawRect( previewRect.Shrink( 1 ), Rounding );
				}
				else if ( IsUnderMouse )
				{
					Paint.SetPen( Color.White.WithAlpha( 0.2f ), 1 );
					Paint.DrawRect( previewRect.Shrink( 1 ), Rounding );
				}

				Paint.ClearBrush();
				Paint.SetPen( Selected ? Color.White : Color.White.WithAlpha( 0.5f ) );
				Paint.SetDefaultFont( 8, Selected ? 600 : 400 );
				Paint.DrawText( labelRect, Label, TextFlag.CenterTop );
			}
		}

		[StructLayout( LayoutKind.Sequential )]
		struct MeshVertex( Vector3 position, Vector3 normal, Vector4 tangent, Vector2 texcoord, Color32 blend, Color32 color )
		{
			[VertexLayout.Position] public Vector3 Position = position;
			[VertexLayout.Normal] public Vector3 Normal = normal;
			[VertexLayout.Tangent] public Vector4 Tangent = tangent;
			[VertexLayout.TexCoord] public Vector2 Texcoord = texcoord;
			[VertexLayout.TexCoord( 4 )] public Color32 Blend = blend;
			[VertexLayout.TexCoord( 5 )] public Color32 Color = color;
		}

		static Mesh CreatePlane( Color32 mask )
		{
			var material = Material.Load( "materials/dev/gray_grid_8.vmat" );
			var mesh = new Mesh( material );
			mesh.CreateVertexBuffer( 4, new[]
			{
				new MeshVertex( new Vector3( -50, -50, 0 ), Vector3.Up, new Vector4( 1, 0, 0, 1 ), new Vector2( 0, 0 ), mask, Color.White ),
				new MeshVertex( new Vector3( 50, -50, 0 ), Vector3.Up,  new Vector4( 1, 0, 0, 1 ), new Vector2( 1, 0 ), mask, Color.White ),
				new MeshVertex( new Vector3( 50, 50, 0 ), Vector3.Up,  new Vector4( 1, 0, 0, 1 ), new Vector2( 1, 1 ), mask, Color.White ),
				new MeshVertex( new Vector3( -50, 50, 0 ), Vector3.Up,  new Vector4( 1, 0, 0, 1 ), new Vector2( 0, 1 ), mask, Color.White ),
			} );
			mesh.CreateIndexBuffer( 6, new[] { 0, 1, 2, 2, 3, 0 } );
			mesh.Bounds = BBox.FromPositionAndSize( 0, 100 );

			return mesh;
		}

		static Pixmap CreateBlendPixmap( Material material, Vector2 size, Vector4 mask )
		{
			var world = new SceneWorld();

			var camera = new SceneCamera
			{
				BackgroundColor = Color.Black,
				Ortho = true,
				Rotation = Rotation.FromPitch( 90 ),
				Position = Vector3.Up * 200,
				OrthoHeight = 100,
				World = world
			};

			var light = new SceneLight( world )
			{
				Radius = 4000,
				LightColor = Color.White * 0.8f,
				Position = new Vector3( 0, 0, 100 ),
				ShadowsEnabled = true
			};

			var mesh = CreatePlane( new Color( mask.x, mask.y, mask.z, mask.w ) );
			var model = Model.Builder
				.AddMesh( mesh )
				.Create();

			var obj = new SceneObject( world, model );
			obj.Transform = new Transform
			{
				Position = Vector3.Zero,
				Rotation = Rotation.From( 0, 90, 0 ),
				Scale = new Vector3( 1, size.x / size.y, 1 )
			};

			obj.SetMaterialOverride( material );

			var pixmap = new Pixmap( size );
			camera.RenderToPixmap( pixmap );

			world.Delete();
			camera.Dispose();

			return pixmap;
		}
	}
}
