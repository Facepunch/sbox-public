using Button = Sandbox.Mapping.Button;

partial class QuakeMap( string pakDir, string fileName ) : SceneLoader<QuakeMount>
{
	public string PakDir { get; set; } = pakDir;
	public string FileName { get; set; } = fileName;

	private List<Texture> Textures { get; init; } = [];
	private List<Texture> FullbrightTextures { get; init; } = [];
	private readonly Dictionary<(int Tex, int Page), Material> _materials = [];

	private readonly List<Texture[]> _lightmapPages = [];
	private Texture _lightstyleTex;
	private FaceSurface[] Surfaces;

	private float _waterAlpha = 0.5f;
	private float _lavaAlpha = 1.0f;
	private float _slimeAlpha = 0.5f;
	private float _teleAlpha = 0.5f;

	private Model _worldModel;
	private readonly Dictionary<int, Model> _brushModels = [];

	private readonly List<PendingDoor> _doors = [];
	private readonly List<(Button Button, string Target)> _buttons = [];

	protected override void BuildScene()
	{
		var file = new Quake.BSP.File( Host.GetFileStream( PakDir, FileName ) );

		_worldModel = BuildWorldModel( file );
		if ( _worldModel is null )
			return;

		var world = new GameObject( true, "worldspawn" );
		world.AddComponent<ModelRenderer>().Model = _worldModel;

		var collider = world.AddComponent<ModelCollider>();
		collider.Model = _worldModel;
		collider.Static = true;

		SpawnEntities( file );
	}
}
