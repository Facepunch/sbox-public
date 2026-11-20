using Sandbox.UI;

/// <summary>
/// Places <see cref="SpeakerEntity"/>s. Either a physical speaker that welds on like an emitter, or a hidden one with
/// no model or collision that just sticks to whatever you shot - find it again in the inspect menu (hold C).
/// </summary>
[Hide]
[Title( "#tool.name.speaker" )]
[Icon( "🔊" )]
[ClassName( "speakertool" )]
[Group( "#tool.group.building" )]
public sealed class SpeakerTool : ToolMode
{
	public const string SpeakerPrefab = "entities/speaker/speaker.prefab";

	public override bool UseSnapGrid => true;
	public override IEnumerable<string> TraceIgnoreTags => ["constraint", "collision"];

	/// <summary>
	/// Place a speaker with no model or collision. It's parented to what you shot, and shows as an icon in the
	/// inspect menu.
	/// </summary>
	[Property, Title( "#tool.setting.speaker_hidden" ), Description( "#tool.setting.speakertool.hidden.description" ), Group( "#tool.setting.group.behavior" ), Order( 0 )]
	public bool Hidden { get; set; } = false;

	// the same settings as SpeakerEntity, put on the placed speaker
	[Property, Title( "#entity.property.mode" ), Description( "#entity.description.speaker_mode" ), Group( "#tool.setting.group.behavior" ), Order( 0 )]
	public SpeakerMode Mode { get; set; } = SpeakerMode.OneShot;

	[Property, ShowIf( nameof( Mode ), SpeakerMode.OneShot ), Title( "#entity.property.speaker_retrigger" ), Description( "#entity.description.speaker_retrigger" ), Group( "#tool.setting.group.behavior" ), Order( 0 )]
	public SpeakerRetrigger Retrigger { get; set; } = SpeakerRetrigger.Restart;

	public bool ShowOverlapLimit => Mode == SpeakerMode.OneShot && Retrigger == SpeakerRetrigger.Overlap;

	[Property, ShowIf( nameof( ShowOverlapLimit ), true ), Range( 1, 16 ), Title( "#entity.property.speaker_max_voices" ), Description( "#entity.description.speaker_max_voices" ), Group( "#tool.setting.group.behavior" ), Order( 0 )]
	public int MaxVoices { get; set; } = 8;

	[Property, Range( 0, 1 ), Title( "#entity.property.volume" ), Description( "#entity.description.speaker_volume" ), Group( "#tool.setting.group.behavior" ), Order( 0 )]
	public float Volume { get; set; } = 1f;

	[Property, Range( 0.25f, 2f ), Title( "#entity.property.pitch" ), Description( "#entity.description.speaker_pitch" ), Group( "#tool.setting.group.behavior" ), Order( 0 )]
	public float Pitch { get; set; } = 1f;

	[Property, Title( "#entity.property.speaker_key" ), Description( "#entity.description.speaker_key" ), Group( "#tool.setting.group.inputs" ), Order( 1 )]
	public ClientInput Key { get; set; }

	// sounds are paths or workshop idents, see SoundResolver
	[Property, ResourceSelect( Extension = "sound", AllowPackages = true, Popup = true ), Title( "#entity.property.speaker_sound" ), Description( "#entity.description.speaker_sound" ), Group( "#tool.setting.group.sound" ), Order( 2 )]
	public string Sound { get; set; } = SpeakerEntity.DefaultSound;

	[Property, Title( "#entity.property.looping" ), Description( "#entity.description.speaker_looping" ), Group( "#tool.setting.group.sound" ), Order( 2 )]
	public bool Looping { get; set; } = false;

	[Property, Range( SpeakerAttenuation.MinRange, SpeakerAttenuation.MaxRange ), Title( "#entity.property.speaker_range" ), Description( "#entity.description.speaker_range" ), Group( "#tool.setting.group.sound" ), Order( 3 )]
	public float SoundRange { get; set; } = SpeakerAttenuation.DefaultRange;

	[Property, Range( SpeakerAttenuation.MinFalloff, SpeakerAttenuation.MaxFalloff ), Title( "#entity.property.speaker_falloff" ), Description( "#entity.description.speaker_falloff" ), Group( "#tool.setting.group.sound" ), Order( 3 )]
	public float Falloff { get; set; } = SpeakerAttenuation.DefaultFalloff;

	[Property, Title( "#tool.setting.speaker_range_preview" ), Description( "#tool.setting.speaker_range_preview.description" ), Group( "#tool.setting.group.sound" ), Order( 4 )]
	public bool ShowRangePreview { get; set; } = true;

	private readonly SpeakerRangePreview _rangePreview = new();

	public override string Description => "#tool.hint.speakertool.description";

	/// <summary>
	/// Half the size of the box previewing a hidden speaker.
	/// </summary>
	private const float HiddenPreviewSize = 3f;

	protected override void OnStart()
	{
		base.OnStart();

		RegisterAction( ToolInput.Primary, () => Hidden ? "#tool.hint.speakertool.place_hidden" : "#tool.hint.speakertool.place", OnPlace );
		RegisterAction( ToolInput.Secondary, () => Hidden ? "#tool.hint.speakertool.place_hidden_world" : "#tool.hint.speakertool.place_no_weld", OnPlaceNoWeld );
		RegisterAction( ToolInput.Reload, () => Hidden ? "#tool.hint.speakertool.show_model" : "#tool.hint.speakertool.hide_model", OnToggleHidden );
	}

	void OnPlace() => Place( attach: true );
	void OnPlaceNoWeld() => Place( attach: false );

	void OnToggleHidden()
	{
		Hidden = !Hidden;
	}

	void Place( bool attach )
	{
		var select = TraceSelect();
		if ( !select.IsValid() ) return;

		Spawn( select, GetPlacementTransform( select ), Hidden, attach, WriteConfiguration<SpeakerEntity>() );
		ShootEffects( select );
	}

	static Transform GetPlacementTransform( SelectionPoint select )
	{
		var surface = select.WorldTransform();
		return new Transform( surface.Position, surface.Rotation );
	}

	public override void OnControl()
	{
		base.OnControl();

		var select = TraceSelect();
		if ( !select.IsValid() ) return;

		var tx = GetPlacementTransform( select );
		if ( ShowRangePreview )
			_rangePreview.Draw( Scene, tx, SoundRange, Falloff, Volume );

		if ( Hidden )
		{
			var size = Vector3.One * HiddenPreviewSize;
			DebugOverlay.Box( new BBox( -size, size ), Color.Cyan.WithAlpha( 0.8f ), transform: tx );
			return;
		}

		var prefab = ResourceLibrary.Get<PrefabFile>( SpeakerPrefab );
		if ( prefab?.GetScene() is not Scene scene ) return;

		DebugOverlay.GameObject( scene, transform: tx, castShadows: true, color: Color.White.WithAlpha( 0.9f ) );
	}

	/// <summary>
	/// Host: make the speaker. A visible one welds to what was hit (unless <paramref name="attach"/> is false);
	/// a hidden one has no physics, so it's parented to it instead (or left in the world).
	/// </summary>
	[Rpc.Host( NetFlags.OwnerOnly )]
	public void Spawn( SelectionPoint point, Transform tx, bool hidden, bool attach, string configuration )
	{
		var input = attach ? ToolInput.Primary : ToolInput.Secondary;
		if ( !FireToolAction( input ) ) return;

		var go = hidden ? CreateHidden( point, tx, attach ) : CreateVisible( point, tx, attach );
		if ( !go.IsValid() ) return;

		ApplyConfiguration( go.GetComponentInChildren<SpeakerEntity>( true ), configuration );

		go.NetworkSpawn( true, null );

		Track( go );

		var undo = Player.Undo.Create();
		undo.Name = "#tool.name.speaker";
		undo.Icon = "🔊";
		undo.Add( go );

		if ( !point.IsWorld )
			CheckContraptionStats( point.GameObject );

		FirePostToolAction( input );
	}

	GameObject CreateVisible( SelectionPoint point, Transform tx, bool weld )
	{
		var prefab = ResourceLibrary.Get<PrefabFile>( SpeakerPrefab );
		if ( prefab?.GetScene() is not Scene scene )
		{
			Log.Warning( $"Speaker tool: can't find {SpeakerPrefab}" );
			return null;
		}

		var go = scene.Clone( new CloneConfig { StartEnabled = false } );
		go.Tags.Add( "removable" );
		go.Tags.Add( "constraint" );
		go.WorldTransform = tx;

		// welds to the world too, like buttons, so it can go on a wall
		if ( weld )
		{
			var joint = go.AddComponent<FixedJoint>();
			joint.Attachment = Joint.AttachmentMode.LocalFrames;
			joint.LocalFrame2 = point.GameObject.WorldTransform.WithScale( 1 ).ToLocal( tx );
			joint.LocalFrame1 = new Transform();
			joint.AngularFrequency = 0;
			joint.LinearFrequency = 0;
			joint.Body = point.GameObject;
			joint.EnableCollision = false;
		}

		ApplyPhysicsProperties( go );
		return go;
	}

	static GameObject CreateHidden( SelectionPoint point, Transform tx, bool parent )
	{
		var go = new GameObject( false, "speaker (hidden)" );
		go.Tags.Add( "removable" );
		go.WorldTransform = tx;
		go.AddComponent<SpeakerEntity>();

		// no physics of its own, so it rides along with what it was stuck to - which also makes it part of that
		// contraption, so seats on it can drive it
		if ( parent && !point.IsWorld )
		{
			var target = point.GameObject.Network?.RootGameObject ?? point.GameObject.Root;
			go.SetParent( target, true );
		}

		return go;
	}
}
