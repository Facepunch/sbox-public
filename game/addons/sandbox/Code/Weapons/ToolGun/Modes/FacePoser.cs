using Sandbox.UI;

/// <summary>
/// Edits facial expressions on a selected model and copies them to other faces.
/// </summary>
[Title( "#ui.faceposer.title" ), Icon( "🙂" ), ClassName( "faceposer" ), Group( "#tool.group.posing" )]
public sealed class FacePoser : ToolMode, ICustomSettingsSheet
{
	SkinnedModelRenderer _selected;
	Scene _draftScene;
	SkinnedModelRenderer _draft;
	readonly Dictionary<string, float> _pending = new();
	internal SkinnedModelRenderer SelectedFace => _selected.IsValid() ? _selected : null;

	/// <summary>
	/// Model to show when editing the pose before selecting a character.
	/// </summary>
	[Property] public string PoseModel
	{
		get;
		set
		{
			if ( string.IsNullOrWhiteSpace( value ) || field == value ) return;
			FlushEdits();
			_selected = null;
			field = value;
		}
	} = "models/citizen_human/human.vmdl";

	/// <summary>
	/// The named morph weights carried by the tool and saved with its presets.
	/// </summary>
	[Property] public Dictionary<string, float> Pose
	{
		get;
		set
		{
			field = value ?? new();
			_pending.Clear();
			if ( _selected.IsValid() )
				SendPose( _selected, ToolInput.Secondary, field, true );
			if ( _draft.IsValid() ) SetDraftPose();
		}
	} = new();

	internal SkinnedModelRenderer EditableFace
	{
		get
		{
			if ( !_draft.IsValid() )
			{
				_draftScene = new Scene { WantsSystemScene = false };
				_draft = new GameObject( _draftScene, true, "Face pose draft" ).AddComponent<SkinnedModelRenderer>();
				_draft.UseAnimGraph = false;
			}
			if ( _draft.Model?.ResourcePath != PoseModel )
			{
				_draft.Model = Model.Load( PoseModel );
				SetDraftPose();
			}
			return _draft;
		}
	}

	void SetDraftPose()
	{
		if ( !_draft.SceneModel.IsValid() ) return;
		var morphs = _draft.SceneModel.Morphs;
		foreach ( var name in _draft.Morphs.Names )
			morphs.Set( name, Pose.GetValueOrDefault( name ), 0f );
	}

	/// <summary>
	/// Builds the live portrait, morph sliders and expression shortcuts in the tool settings.
	/// </summary>
	public void BuildSettingsSheet( Panel body )
	{
		body.AddChild<FacePoseEditor>().Tool = this;
	}

	/// <summary>
	/// Explains how to edit and transfer an expression.
	/// </summary>
	public override string Description => "#tool.hint.faceposer.description";

	protected override void OnStart()
	{
		base.OnStart();
		RegisterAction( ToolInput.Primary, () => "#tool.hint.faceposer.apply", () => UseOnFace( ToolInput.Primary ) );
		RegisterAction( ToolInput.Secondary, () => "#tool.hint.faceposer.select", () => UseOnFace( ToolInput.Secondary ) );
		RegisterAction( ToolInput.Reload, () => "#tool.hint.faceposer.reset", () => UseOnFace( ToolInput.Reload ) );
	}

	void UseOnFace( ToolInput input )
	{
		var hit = TraceSelect();
		if ( !hit.IsValid() || hit.IsWorld ) return;

		var root = hit.GameObject.Network.RootGameObject ?? hit.GameObject.Root;
		var face = root.GetComponentsInChildren<SkinnedModelRenderer>().FirstOrDefault( CanPose );
		if ( !face.IsValid() ) return;

		FlushEdits();
		if ( input == ToolInput.Secondary )
		{
			_selected = null;
			PoseModel = face.Model.ResourcePath;
			Pose = face.Morphs.Names.ToDictionary( n => n, n => face.SceneModel.Morphs.Get( n ) );
			_selected = face;
		}
		else if ( input == ToolInput.Reload )
		{
			if ( face == _selected )
			{
				Pose.Clear();
				if ( _draft.IsValid() ) SetDraftPose();
			}
			SendPose( face, input, new(), true );
		}
		else
		{
			bool sameRig = face.Model.ResourcePath == PoseModel;
			var weights = sameRig ? Pose : FacePoseMorphs.Transfer( Model.Load( PoseModel ), Pose, face.Model );
			if ( !sameRig && weights.Count == 0 ) return;
			SendPose( face, input, weights, sameRig );
		}
		ShootEffects( hit );
	}

	static bool CanPose( SkinnedModelRenderer face )
	{
		if ( !face.IsValid() || !face.SceneModel.IsValid() || face.Model?.Morphs.Count is not > 0 || face.BoneMergeTarget.IsValid() ) return false;
		var root = face.GameObject.Network.RootGameObject ?? face.GameObject.Root;
		return !root.Tags.Has( "world" ) && !root.GetComponentInParent<Player>().IsValid();
	}

	internal void EditMorph( SkinnedModelRenderer face, string name, float value )
	{
		if ( face != _draft ) return;
		Pose[name] = value;
		if ( _selected.IsValid() ) _pending[name] = value;
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		FlushEdits();
	}

	void FlushEdits()
	{
		if ( _pending.Count == 0 ) return;
		if ( _selected.IsValid() ) SendPose( _selected, ToolInput.Secondary, _pending, false );
		_pending.Clear();
	}

	void SendPose( SkinnedModelRenderer face, ToolInput input, Dictionary<string, float> weights, bool reset )
	{
		ApplyPose( face, input, Json.Serialize( weights ), reset );
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	void ApplyPose( SkinnedModelRenderer face, ToolInput input, string json, bool reset )
	{
		if ( !CanPose( face ) || face.IsProxy || !face.GameObject.HasAccess( Rpc.Caller ) || !FireToolAction( input ) ) return;
		var weights = Json.Deserialize<Dictionary<string, float>>( json );
		if ( weights is null || weights.Values.Any( v => !float.IsFinite( v ) ) ) return;
		var supported = face.Morphs.Names.Where( n => reset || weights.ContainsKey( n ) )
			.ToDictionary( n => n, n => weights.GetValueOrDefault( n ).Clamp( 0f, 1f ) );
		var state = face.GameObject.GetOrAddComponent<MorphState>();
		if ( reset )
		{
			state.ApplyPreset( Json.Serialize( supported ) );
		}
		else
		{
			state.ApplyBatch( Json.Serialize( supported ) );
		}
		FirePostToolAction( input );
	}

	protected override void OnDisabled()
	{
		FlushEdits();
		_selected = null;
		base.OnDisabled();
	}

	protected override void OnDestroy()
	{
		_draftScene?.Destroy();
		base.OnDestroy();
	}
}
