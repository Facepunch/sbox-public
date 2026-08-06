using Button = Sandbox.Mapping.Button;

partial class QuakeMap : SceneLoader<QuakeMount>
{
	public string PakDir { get; init; }
	public string FileName { get; init; }

	private List<Texture> Textures { get; init; } = [];
	private List<Texture> FullbrightTextures { get; init; } = [];
	private readonly Dictionary<(int Tex, int Page), Material> _materials = [];

	private readonly List<Texture[]> _lightmapPages = [];
	private Texture _lightstyleTex;
	private FaceSurface[] Surfaces;

	private bool _hasSkybox = false;

	private float _waterAlpha = 0.5f;
	private float _lavaAlpha = 1.0f;
	private float _slimeAlpha = 0.5f;
	private float _teleAlpha = 0.5f;

	private Model _worldModel;
	private readonly Dictionary<int, Model> _brushModels = [];

	private readonly List<PendingDoor> _doors = [];
	private readonly List<(Button Button, string Target)> _buttons = [];

	public QuakeMap( string pakDir, string fileName )
	{
		PakDir = pakDir;
		FileName = fileName;

		// hide stuff like b_shell1.bsp which isn't a map, it's a model
		string fn = System.IO.Path.GetFileName( FileName );
		Flags = Flags.WithFlag( ResourceFlags.DeveloperOnly, fn.StartsWith( "b_" ) );
	}

	protected override void BuildScene()
	{
		var file = new Quake.BSP.File( Host.GetFileStream( PakDir, FileName ) );

		var world = new GameObject( true, "worldspawn" );
		var worldspawn = file.Entities.First();
		if ( worldspawn.TryGetValue( "_sky", out string skybox ) )
		{
			if ( skybox[skybox.Length - 1] == '_' )
				skybox = skybox[..^1];

			var skybox2d = world.AddComponent<SkyBox2D>();
			skybox2d.SkyIndirectLighting = false;
			skybox2d.SkyMaterial = Material.Load( $"mount://{Host.Ident}/{PakDir}/gfx/env/{skybox}.vmat" );
			_hasSkybox = true;
		}

		_worldModel = BuildWorldModel( file );
		if ( _worldModel is null )
			return;

		world.AddComponent<ModelRenderer>().Model = _worldModel;

		var collider = world.AddComponent<ModelCollider>();
		collider.Model = _worldModel;
		collider.Static = true;

		SpawnEntities( file );
	}
}
