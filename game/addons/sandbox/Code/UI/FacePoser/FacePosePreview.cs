using Sandbox.UI;

namespace Sandbox;

/// <summary>
/// A portrait of the selected model with handles for its facial morphs.
/// </summary>
public sealed class FacePosePreview : ScenePanel
{
	/// <summary>
	/// The tool panel that owns the portrait and sends pose edits.
	/// </summary>
	[Parameter] public FacePoseEditor Editor { get; set; }

	enum Feature { Brow, Lid, Mouth, Jaw }

	sealed record Handle( Feature Feature, Panel Dot, Vector3 Position, string[] Up, string[] Down );

	readonly List<Handle> _handles = new();
	SkinnedModelRenderer _source;
	SkinnedModelRenderer _appearance;
	SkinnedModelRenderer _portrait;
	CameraComponent _camera;
	Vector3 _center;
	Vector3 _front;
	float[] _weights;
	float _yaw;
	float _travel;
	Handle _drag;
	bool _orbiting;
	Vector2 _start;
	float _startValue;

	void SetTarget( SkinnedModelRenderer source, SkinnedModelRenderer appearance )
	{
		DeleteChildren( true );
		_handles.Clear();
		_drag = null;
		_orbiting = false;
		_yaw = 0f;
		_portrait?.GameObject.Destroy();
		_source = source;
		_appearance = appearance;

		using var scope = RenderScene.Push();
		if ( !_camera.IsValid() )
		{
			RenderOnce = true;
			_camera = new GameObject( true, "Portrait camera" ).AddComponent<CameraComponent>();
			_camera.Orthographic = true;
			_camera.BackgroundColor = new Color( 0.14f, 0.19f, 0.23f );
			new GameObject( true, "Fill" ).AddComponent<AmbientLight>().Color = Color.White * 0.7f;
			var key = new GameObject( true, "Key" ).AddComponent<DirectionalLight>();
			key.WorldRotation = Rotation.LookAt( new Vector3( -1f, 0.45f, -0.5f ) );
			key.LightColor = Color.White * 1.3f;
			key.Shadows = false;
		}

		_portrait = new GameObject( true, "Portrait" ).AddComponent<SkinnedModelRenderer>();
		CopyAppearance( appearance, _portrait );
		foreach ( var item in appearance.GameObject.GetComponentsInChildren<SkinnedModelRenderer>() )
		{
			if ( !item.Active || item.BoneMergeTarget != appearance ) continue;
			var clothing = new GameObject( _portrait.GameObject, true, item.GameObject.Name ).AddComponent<SkinnedModelRenderer>();
			CopyAppearance( item, clothing );
			clothing.BoneMergeTarget = _portrait;
		}

		var model = source.Model;
		_weights = new float[model.Morphs.Count];
		Array.Fill( _weights, float.NaN );
		var head = model.Bones.GetBone( "head" ) ?? model.Bones.GetBone( "ValveBiped.Bip01_Head1" );
		var left = model.Bones.GetBone( "eye_L" );
		var right = model.Bones.GetBone( "eye_R" );
		_center = model.Bounds.Center;
		_front = Vector3.Forward;
		var height = model.Bounds.Size.Length;

		if ( head is not null )
		{
			var chin = head.LocalTransform.Position;
			height = MathF.Max( model.Bounds.Maxs.z - chin.z, 1f ) * 1.65f;
			_center = chin + Vector3.Up * height * 0.22f;
			if ( left is not null && right is not null )
			{
				var a = left.LocalTransform.Position;
				var b = right.LocalTransform.Position;
				var eyes = (a + b) * 0.5f;
				var depth = MathF.Max( eyes.z - chin.z, 0.5f );
				AddFaceHandles( a, b, eyes + Vector3.Down * depth * 0.66f, depth );
			}
		}

		// Source humanoids attach a single eye midpoint to the head. The attachment also
		// tells us which way the mesh faces in bind pose.
		if ( (left is null || right is null) && model.Attachments.GetTransform( "eyes" ) is { } eyeFrame
			&& model.Attachments.GetTransform( "mouth" ) is { } mouthFrame )
		{
			var depth = (eyeFrame.Position.z - mouthFrame.Position.z) / 0.66f;
			var facing = eyeFrame.Forward.WithZ( 0f ).Normal;
			if ( depth > 0.1f && !facing.IsNearZeroLength )
			{
				_front = facing;
				var halfSpacing = eyeFrame.Left * depth * 0.28f;
				AddFaceHandles( eyeFrame.Position + halfSpacing, eyeFrame.Position - halfSpacing, mouthFrame.Position, depth );
				_center = eyeFrame.Position + Vector3.Down * depth * 0.28f;
				height = depth * 3.1f;
			}
		}

		_camera.OrthographicHeight = MathF.Max( height, 1f );
		_camera.ZNear = 0.1f;
		_camera.ZFar = _camera.OrthographicHeight * 4f;
		UpdateView();

		if ( _handles.Count == 0 ) return;
		var row = AddChild<Panel>( "expressions" );
		AddExpression( row, "😐", 0, 0, 0, 0 );
		AddExpression( row, "🙂", 0.15f, 0, 1, 0 );
		AddExpression( row, "☹️", 0.35f, 0, -1, 0 );
		AddExpression( row, "😠", -1, -0.15f, -0.5f, 0 );
		AddExpression( row, "😮", 1, 0.85f, 0, -0.85f );
	}

	static void CopyAppearance( SkinnedModelRenderer source, SkinnedModelRenderer copy )
	{
		copy.UseAnimGraph = false;
		copy.Model = source.Model;
		copy.BodyGroups = source.BodyGroups;
		copy.Tint = source.Tint;
		copy.MaterialGroup = source.MaterialGroup;
		copy.MaterialOverride = source.MaterialOverride;
		for ( int i = 0; i < source.Materials.Count; i++ )
		{
			if ( source.Materials.GetOverride( i ) is { } material )
				copy.Materials.SetOverride( i, material );
		}
	}

	void AddFaceHandles( Vector3 left, Vector3 right, Vector3 mouth, float depth )
	{
		_travel = depth * 0.12f;
		var center = (left + right) * 0.5f;
		foreach ( var (eye, side) in new[] { (left, "L"), (right, "R") } )
		{
			AddHandle( Feature.Brow, eye + Vector3.Up * depth * 0.24f, FacePoseMorphs.BrowUp( side ), FacePoseMorphs.BrowDown( side ) );
			AddHandle( Feature.Lid, eye, FacePoseMorphs.LidUp( side ), FacePoseMorphs.LidDown( side ) );
			AddHandle( Feature.Mouth, mouth + (eye - center) * 0.88f, FacePoseMorphs.Smile( side ), FacePoseMorphs.Frown( side ) );
		}
		AddHandle( Feature.Jaw, mouth + Vector3.Down * depth * 0.34f, [], FacePoseMorphs.OpenJaw );
	}

	void AddHandle( Feature feature, Vector3 anchor, string[] up, string[] down )
	{
		var names = _source.Morphs.Names;
		up = ResolveNames( up );
		down = ResolveNames( down );
		if ( up.Length + down.Length == 0 ) return;

		var dot = AddChild<Panel>( "face-handle" );
		dot.AddChild<Panel>( "dot" );
		_handles.Add( new( feature, dot, anchor, up, down ) );

		// Pose data uses the spelling from the model, even if an alias differs in case.
		string[] ResolveNames( string[] aliases ) => aliases
			.Select( alias => Array.Find( names, name => name.Equals( alias, StringComparison.OrdinalIgnoreCase ) ) )
			.Where( name => name is not null ).ToArray();
	}

	void AddExpression( Panel row, string emoji, float brow, float lid, float mouth, float jaw )
	{
		var morphs = new Dictionary<string, float>();
		foreach ( var handle in _handles )
		{
			var weight = handle.Feature switch { Feature.Brow => brow, Feature.Lid => lid, Feature.Mouth => mouth, _ => jaw };
			if ( weight == 0f ) continue;
			foreach ( var name in weight > 0f ? handle.Up : handle.Down )
				morphs[name] = MathF.Abs( weight );
		}

		if ( morphs.Count == 0 && emoji != "😐" ) return;
		var button = row.AddChild<Label>( "expression" );
		button.Text = emoji;
		button.AddEventListener( "onclick", e =>
		{
			if ( _source == Editor.Target ) Editor.ApplyExpression( morphs );
			e.StopPropagation();
		} );
	}

	void UpdateView()
	{
		var forward = Rotation.FromYaw( _yaw ) * -_front;
		_camera.WorldRotation = Rotation.LookAt( forward );
		_camera.WorldPosition = _center - forward * _camera.OrthographicHeight * 1.5f;
		RenderNextFrame();
	}

	float Weight( Handle handle )
	{
		var morphs = _source.SceneModel.Morphs;
		return (handle.Up.Length > 0 ? morphs.Get( handle.Up[0] ) : 0f)
			- (handle.Down.Length > 0 ? morphs.Get( handle.Down[0] ) : 0f);
	}

	void SetWeight( Handle handle, float value )
	{
		foreach ( var name in handle.Up ) Set( name, value.Clamp( 0f, 1f ) );
		foreach ( var name in handle.Down ) Set( name, (-value).Clamp( 0f, 1f ) );

		void Set( string name, float weight )
		{
			if ( _source.SceneModel.Morphs.Get( name ) != weight )
				Editor.SetMorph( name, weight );
		}
	}

	internal void SetMorph( SkinnedModelRenderer source, string name, float value )
	{
		if ( source != _source || !_portrait.IsValid() || source.Model != _portrait.Model ) return;
		UpdateMorph( source.Model.Morphs.GetIndex( name ), value );
	}

	void UpdateMorph( int index, float value )
	{
		if ( _weights[index] == value ) return;
		_weights[index] = value;
		_portrait.SceneModel.Morphs.Set( index, value, 0f );
		RenderNextFrame();
	}

	/// <summary>
	/// Mirrors the rendered morph weights and projects the handles into the portrait.
	/// </summary>
	public override void Tick()
	{
		var source = Editor?.Target;
		var size = Box.Rect.Size;
		if ( !IsVisible || size.x <= 0f || size.y <= 0f || !source.IsValid() || !source.SceneModel.IsValid() ) return;
		var appearance = Editor.Tool?.SelectedFace;
		if ( appearance?.Model != source.Model ) appearance = source;
		if ( source != _source || appearance != _appearance || source.Model != _portrait?.Model ) SetTarget( source, appearance );
		_camera.CustomSize = size;
		if ( !HasActive )
		{
			_drag = null;
			_orbiting = false;
		}

		if ( _drag is not null || _orbiting ) DragTo( MousePosition );
		for ( int i = 0; i < _weights.Length; i++ )
		{
			UpdateMorph( i, source.SceneModel.Morphs.Get( i ) );
		}

		foreach ( var handle in _handles )
		{
			var weight = Weight( handle );
			var delta = handle.Position + Vector3.Up * (_travel * weight) - _center;
			var offset = new Vector2( Vector3.Dot( delta, _camera.WorldRotation.Right ), -delta.z );
			var point = (size * 0.5f + offset * size.y / _camera.OrthographicHeight) * ScaleFromScreen;
			handle.Dot.Style.Left = point.x;
			handle.Dot.Style.Top = point.y;
			handle.Dot.SetClass( "posed", weight != 0f );
		}

		base.Tick();
	}

	void DragTo( Vector2 cursor )
	{
		var delta = cursor - _start;
		if ( _drag is not null )
		{
			SetWeight( _drag, _startValue - delta.y / (Box.Rect.Height * 0.2f) );
		}
		else
		{
			var yaw = (_startValue + delta.x * 140f / Box.Rect.Width).Clamp( -65f, 65f );
			if ( yaw == _yaw ) return;
			_yaw = yaw;
			UpdateView();
		}
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );
		if ( _source != Editor?.Target || !_source.IsValid() ) return;
		var handle = _handles.Find( h => h.Dot == e.Target );
		if ( handle is null && e.Target != this ) return;

		if ( e.MouseButton == MouseButtons.Right && handle is not null )
		{
			SetWeight( handle, 0f );
		}
		else if ( e.MouseButton == MouseButtons.Left )
		{
			_drag = handle;
			_orbiting = handle is null;
			_start = EventPosition( e );
			_startValue = _orbiting ? _yaw : Weight( handle );
		}
		e.StopPropagation();
	}

	protected override void OnMouseUp( MousePanelEvent e )
	{
		base.OnMouseUp( e );
		if ( e.MouseButton != MouseButtons.Left || (_drag is null && !_orbiting) ) return;
		if ( _source.IsValid() && _source == Editor?.Target ) DragTo( EventPosition( e ) );
		_drag = null;
		_orbiting = false;
		e.StopPropagation();
	}

	Vector2 EventPosition( MousePanelEvent e ) => e.LocalPosition + e.Target.Box.Rect.Position - Box.Rect.Position;
}
