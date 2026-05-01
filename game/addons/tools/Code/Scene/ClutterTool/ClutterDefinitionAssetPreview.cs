using Editor.Assets;
using Sandbox.Clutter;
using System.Threading.Tasks;

namespace Editor;

/// <summary>
/// Preview for ClutterDefinition using the actual clutter system.
/// </summary>
[AssetPreview( "clutter" )]
public class ClutterDefinitionAssetPreview( Asset asset ) : AssetPreview( asset )
{
	private ClutterDefinition _clutter;
	private GameObject _clutterObject;
	private GameObject _ground;
	private TileBoundsGrid _tileBounds;
	private int _lastHash;
	private int _currentSeed = 1337;

	public override float PreviewWidgetCycleSpeed => 0.15f;

	public override async Task InitializeAsset()
	{
		await base.InitializeAsset();

		_clutter = Asset.LoadResource<ClutterDefinition>();
		if ( _clutter == null ) return;

		using ( Scene.Push() )
		{
			CreateGround();
			CreateTileBounds();
			CreateClutter();
			_lastHash = GetHash();

			await Task.Delay( 50 );
			CalculateSceneBounds();
		}
	}

	public override void UpdateScene( float cycle, float timeStep )
	{
		if ( _clutter != null )
		{
			var currentHash = GetHash();
			if ( currentHash != _lastHash )
			{
				_lastHash = currentHash;
				RegenerateAsync();
			}
		}

		using ( Scene.Push() )
		{
			var angle = cycle * 360.0f;
			var distance = MathX.SphereCameraDistance( SceneSize.Length * 0.5f, Camera.FieldOfView );
			var aspect = (float)ScreenSize.x / ScreenSize.y;
			if ( aspect > 1 ) distance *= aspect;

			var rotation = new Angles( 20, 180 + 45 + angle, 0 ).ToRotation();
			Camera.WorldRotation = rotation;
			Camera.WorldPosition = SceneCenter + rotation.Forward * -distance;
		}

		TickScene( timeStep );
	}

	private async void RegenerateAsync()
	{
		using ( Scene.Push() )
		{
			_clutterObject?.Destroy();
			_tileBounds?.Delete();
			CreateTileBounds();
			CreateClutter();

			await Task.Delay( 50 );
			CalculateSceneBounds();
		}
	}

	private void CreateClutter()
	{
		_clutterObject = new GameObject( true, "ClutterPreview" );

		var component = _clutterObject.Components.Create<ClutterComponent>();
		component.Clutter = _clutter;
		component.Mode = ClutterComponent.ClutterMode.Volume;
		component.Seed = _currentSeed;

		var tileSize = _clutter.TileSize;
		component.Bounds = new BBox(
			new Vector3( -tileSize / 2, -tileSize / 2, -100 ),
			new Vector3( tileSize / 2, tileSize / 2, 100 )
		);
		component.Generate();
	}

	private void CreateGround()
	{
		_ground = new GameObject( true, "Ground" );
		var collider = _ground.Components.Create<BoxCollider>();
		collider.Scale = new Vector3( 5000, 5000, 10 );
		collider.Center = new Vector3( 0, 0, -5 );
	}

	private void CreateTileBounds()
	{
		_tileBounds = new TileBoundsGrid( Scene.SceneWorld, _clutter.TileSize );
	}

	private void CalculateSceneBounds()
	{
		if ( _clutter == null ) return;

		var tileSize = _clutter.TileSize;
		var bbox = new BBox(
			new Vector3( -tileSize / 2, -tileSize / 2, -100 ),
			new Vector3( tileSize / 2, tileSize / 2, 100 )
		);

		bbox = bbox.AddPoint( Vector3.Zero );

		SceneCenter = bbox.Center;
		SceneSize = bbox.Size;
	}

	private int GetHash()
	{
		if ( _clutter == null ) return 0;

		var hash = new System.HashCode();
		hash.Add( _clutter.Entries?.Count ?? 0 );
		hash.Add( _clutter.TileSize );
		hash.Add( _clutter.Scatterer.Value?.GetHashCode() ?? 0 );

		if ( _clutter.Entries != null )
		{
			foreach ( var entry in _clutter.Entries )
			{
				hash.Add( entry?.Weight ?? 0 );
				hash.Add( entry?.Model?.ResourcePath ?? "" );
				hash.Add( entry?.Prefab?.GetHashCode() ?? 0 );
			}
		}

		return hash.ToHashCode();
	}

	public override void Dispose()
	{
		_clutterObject?.Destroy();
		_ground?.Destroy();
		_tileBounds?.Delete();
		base.Dispose();
	}

	public override Widget CreateToolbar()
	{
		var toolbar = new Widget { Layout = Layout.Row() };
		toolbar.Layout.Spacing = 8;
		toolbar.Layout.Margin = 8;

		var randomBtn = new Button( "Randomize", "casino" );
		randomBtn.Clicked += async () =>
		{
			using ( Scene.Push() )
			{
				_currentSeed = Game.Random.Next();
				_clutterObject?.Destroy();
				_tileBounds?.Delete();

				CreateTileBounds();
				CreateClutter();

				await Task.Delay( 50 );
				CalculateSceneBounds();
			}
		};
		randomBtn.ToolTip = "Randomize seed";
		toolbar.Layout.Add( randomBtn );

		return toolbar;
	}
}

/// <summary>
/// Custom scene object that renders tile bounds
/// </summary>
internal class TileBoundsGrid : SceneCustomObject
{
	private readonly Vertex[] _vertices;
	private static readonly Lazy<Material> _lineMaterial = new( () => Material.Load( "materials/gizmo/line.vmat" ) );

	public TileBoundsGrid( SceneWorld world, float tileSize ) : base( world )
	{
		var halfSize = tileSize / 2f;
		var color = Color.Gray.WithAlpha( 0.5f );
		var corners = new Vector3[]
		{
			new Vector3( -halfSize, -halfSize, 0.5f ),
			new Vector3(  halfSize, -halfSize, 0.5f ),
			new Vector3(  halfSize,  halfSize, 0.5f ),
			new Vector3( -halfSize,  halfSize, 0.5f )
		};

		_vertices = new Vertex[8];
		for ( int i = 0; i < 4; i++ )
		{
			_vertices[i * 2] = new Vertex( corners[i], color );
			_vertices[i * 2 + 1] = new Vertex( corners[(i + 1) % 4], color );
		}

		Bounds = new BBox( new Vector3( -halfSize, -halfSize, 0 ), new Vector3( halfSize, halfSize, 10 ) );
	}

	public override void RenderSceneObject()
	{
		Graphics.Draw( _vertices.AsSpan(), _vertices.Length, _lineMaterial.Value, Attributes, Graphics.PrimitiveType.Lines );
	}
}
