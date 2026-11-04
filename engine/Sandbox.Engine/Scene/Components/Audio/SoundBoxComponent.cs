namespace Sandbox;

/// <summary>
/// Plays a sound within a box.
/// </summary>
[Expose]
[Category( "Audio" )]
[Title( "Sound Box" )]
[Icon( "surround_sound" )]
[EditorHandle( "materials/gizmo/sound.png" )]
[Tint( EditorTint.Green )]
public sealed class SoundBoxComponent : BaseSoundComponent
{
	[Property, Title( "Box Size" ), Group( "Box" )]
	public Vector3 Scale
	{
		get => _scale;
		set
		{
			if ( _scale == value ) return;

			_scale = value;
		}
	}
	Vector3 _scale = 50;

	public BBox Inner => new BBox( WorldPosition + Scale * -0.5f, WorldPosition + Scale * 0.5f );

	public Vector3 SndPos { get; private set; }

	protected override void DrawGizmos()
	{
		if ( !Gizmo.IsSelected && !Gizmo.IsHovered )
			return;

		Gizmo.Draw.LineThickness = 1;
		Gizmo.Draw.Color = Gizmo.Colors.Green.WithAlpha( Gizmo.IsSelected ? 1.0f : 0.2f );
		Gizmo.Draw.LineBBox( new BBox( Scale * -0.5f, Scale * 0.5f ) );

		Gizmo.Draw.Color = Gizmo.Colors.Red.WithAlpha( 0.75f );
		Gizmo.Draw.LineSphere( SndPos - LocalPosition, 10.0f );
	}

	protected override void OnEnabled()
	{
		if ( PlayOnStart )
		{
			StartSound();
		}
	}

	TimeUntil TimeUntilRepeat;

	public override void StartSound()
	{
		if ( StopOnNew )
		{
			SoundHandle?.Stop( 0.1f );
			SoundHandle = default;
		}

		if ( SoundHandle.IsValid() && SoundHandle.IsPlaying ) return;

		SoundHandle = Sound.Play( SoundEvent );

		if ( SoundHandle.IsValid() )
		{
			SoundHandle.Position = SndPos;
			ApplyOverrides( SoundHandle );
		}

		TimeUntilRepeat = Random.Shared.Float( MinRepeatTime, MaxRepeatTime );
	}

	protected override void OnUpdate()
	{
		if ( SoundHandle is not null )
		{
			SoundHandle.Position = SndPos;
			ApplyOverrides( SoundHandle );
		}

		ShortestDistanceToSurface();

		if ( Repeat && TimeUntilRepeat <= 0.0f )
		{
			StartSound();
		}
	}

	public override void StopSound()
	{
		SoundHandle?.Stop( 0.1f );
	}

	protected override void OnDestroy()
	{
		base.OnDestroy();
		StopSound();
	}

	private void ShortestDistanceToSurface()
	{
		var listener = Scene.FindClosestListener( Inner.Center );
		SndPos = Inner.ClosestPoint( listener?.Position ?? default );
	}
}
