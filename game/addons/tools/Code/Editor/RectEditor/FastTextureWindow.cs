using Editor.MeshEditor;

namespace Editor.RectEditor;

public class FastTextureWindow : Window
{
	public MeshFace[] MeshFaces { get; private set; }

	public FastTextureWindow() : base()
	{
		Size = new Vector2( 900, 700 );
		Settings.IsFastTextureTool = true;
	}

	protected override void BuildDock()
	{
		DockManager.RegisterDockType( "Rect View", "space_dashboard", null, false );
		RectView = new RectView( this );
		DockManager.AddDock( null, RectView, DockArea.Right, DockManager.DockProperty.HideOnClose, 0.0f );

		DockManager.RegisterDockType( "Properties", "edit", null, false );
		Properties = new Properties( this );
		UpdateProperties();
		DockManager.AddDock( null, Properties, DockArea.Left, DockManager.DockProperty.HideOnClose, 0.25f );

		ToolBar.Visible = false;
	}

	public static void OpenWith( MeshFace[] faces, Material material = null )
	{
		var window = new FastTextureWindow();
		window.MeshFaces = faces;
		window.Parent = EditorWindow;

		window.InitializeWithFaces( faces, material );
		window.WindowFlags = WindowFlags.Dialog | WindowFlags.Customized | WindowFlags.CloseButton | WindowFlags.WindowSystemMenuHint | WindowFlags.WindowTitle | WindowFlags.MaximizeButton;
		window.WindowTitle = "Fast Texturing Tool";
		window.Show();
	}

	private void InitializeWithFaces( MeshFace[] faces, Material material )
	{
		MeshFaces = faces;
		InitRectanglesFromMeshFaces();

		Settings.ReferenceMaterial = material?.ResourcePath;
		RectView.SetMaterial( material );
	}

	protected override void InitRectanglesFromMeshFaces()
	{
		if ( MeshFaces is null || MeshFaces.Length == 0 )
			return;

		var meshRect = Document.AddMeshRectangle( this, MeshFaces, Settings.FastTextureSettings );
		Document.SelectRectangle( meshRect, SelectionOperation.Set );
		OnDocumentModified();
	}

	protected override void UpdateMeshFaces()
	{
		if ( MeshFaces is null || MeshFaces.Length == 0 )
			return;

		var meshRect = Document.Rectangles.OfType<Document.MeshRectangle>().FirstOrDefault();
		if ( meshRect != null && meshRect.FaceVertexIndices.Count == MeshFaces.Length )
		{
			var rectangleRelativePositions = meshRect.GetRectangleRelativePositions();

			for ( int faceIndex = 0; faceIndex < MeshFaces.Length; faceIndex++ )
			{
				var face = MeshFaces[faceIndex];
				var vertexIndices = meshRect.FaceVertexIndices[faceIndex];

				var uvs = new Vector2[vertexIndices.Count];
				for ( int i = 0; i < vertexIndices.Count; i++ )
				{
					var vertexIndex = vertexIndices[i];
					if ( vertexIndex < rectangleRelativePositions.Count )
					{
						uvs[i] = rectangleRelativePositions[vertexIndex];
					}
				}

				face.TextureCoordinates = uvs;
			}
		}
	}

	protected override void UpdateTitle()
	{
		WindowTitle = "Fast Texturing Tool";
	}

	protected override void UpdateProperties()
	{
		if ( !Properties.IsValid() )
			return;

		Properties.SerializedObject = Settings.GetSerialized();
	}

	protected override void OnReferenceChanged( Asset asset )
	{
		var material = asset?.LoadResource<Material>();
		Settings.ReferenceMaterial = material?.ResourcePath;
		RectView.SetMaterial( material );

		if ( MeshFaces != null )
		{
			foreach ( var face in MeshFaces )
			{
				face.Material = material;
			}
		}
	}

	public override void OnFastTextureSettingsChanged()
	{
		if ( MeshFaces == null )
			return;

		var meshRect = Document.Rectangles.OfType<Document.MeshRectangle>().FirstOrDefault();
		if ( meshRect != null )
		{
			meshRect.ApplyMapping( Settings.FastTextureSettings, false );
			UpdateMeshFaces();
			Update();
		}
	}

	public override void OnMappingModeChanged()
	{
		if ( MeshFaces == null )
			return;

		var meshRect = Document.Rectangles.OfType<Document.MeshRectangle>().FirstOrDefault();
		if ( meshRect != null )
		{
			bool shouldResetBounds = meshRect.PreviousMappingMode == MappingMode.UseExisting
								  && Settings.FastTextureSettings.Mapping != MappingMode.UseExisting;

			meshRect.ApplyMapping( Settings.FastTextureSettings, shouldResetBounds );
			UpdateMeshFaces();
			Update();
		}
	}

	[EditorEvent.Frame]
	private void OnFrame()
	{
		var selectedFaces = SceneEditorSession.Active.Selection.OfType<MeshFace>().ToArray();
		if ( selectedFaces.Length != MeshFaces.Length || !selectedFaces.All( x => MeshFaces.Contains( x ) ) )
		{
			Close();
		}
	}
}
