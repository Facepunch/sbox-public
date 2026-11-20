using Sandbox.UI;
using System.Diagnostics;

namespace Sandbox;

/// <summary>Virtualized, render-once thumbnails; a material change explicitly requests a new render.</summary>
public sealed class BuilderThumbnail : ScenePanel
{
	[Parameter] public string Data { get; set; }
	[Parameter] public Material PreviewMaterial { get; set; }
	ModelRenderer renderer;
	string previousData;
	Material previousMaterial;
	int request;
	bool dirty;

	public bool IsReady { get; private set; }

	public BuilderThumbnail()
	{
		RenderOnce = true;
	}

	void EnsureScene()
	{
		if ( renderer.IsValid() ) return;

		using ( RenderScene.Push() )
		{
			renderer = new GameObject().AddComponent<ModelRenderer>();
			var camera = new GameObject().AddComponent<CameraComponent>();
			camera.WorldPosition = new Vector3( 2, -3, 2 );
			camera.WorldRotation = Rotation.LookAt( -camera.WorldPosition );
			camera.FieldOfView = 24;
			camera.BackgroundColor = Color.Transparent;
			camera.ZNear = 0.01f;
			new GameObject().AddComponent<AmbientLight>().Color = Color.White * 0.45f;
			var light = new GameObject().AddComponent<DirectionalLight>();
			light.WorldRotation = new Angles( 45, 25, 0 );
			light.LightColor = Color.White * 1.5f;
		}
	}

	protected override void OnParametersSet()
	{
		base.OnParametersSet();
		if ( Data == previousData && PreviewMaterial == previousMaterial ) return;

		// A new piece needs its skeleton back, a new material on the same piece keeps the old render up meanwhile
		if ( Data != previousData ) IsReady = false;

		previousData = Data;
		previousMaterial = PreviewMaterial;
		dirty = true;
	}

	public override void OnHotloaded()
	{
		base.OnHotloaded();
		dirty = true;
	}

	public override void Tick()
	{
		base.Tick();

		if ( !dirty || !IsVisible ) return;
		if ( !ThumbnailLoader.TryTakeSlot() ) return;

		var start = Stopwatch.GetTimestamp();
		dirty = false;
		EnsureScene();
		UpdatePreview();
		ThumbnailLoader.Spend( start );
	}

	async void UpdatePreview()
	{
		int version = ++request;
		var parts = (Data ?? "").Split( '|', 2 );
		if ( BuilderCatalog.Find( parts[0] ) is not { } piece )
		{
			IsReady = true;
			return;
		}

		var model = BuilderGeometry.GetModel( piece );
		var material = PreviewMaterial ?? await BuilderSpawner.LoadMaterial( parts.Length == 2 ? parts[1] : BuilderCatalog.DefaultMaterial );
		if ( !IsValid || version != request ) return;

		// set it all at once after the await, so a slow material never shows the old piece's material on the new piece
		renderer.Model = model;
		float scale = 1 / MathF.Max( model.Bounds.Size.Length, 1 );
		renderer.WorldScale = scale;
		renderer.WorldPosition = -model.Bounds.Center * scale;
		renderer.MaterialOverride = material;
		RenderNextFrame();
		IsReady = true;
	}
}
