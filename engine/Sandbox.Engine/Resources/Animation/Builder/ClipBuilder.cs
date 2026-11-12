using System.Runtime.InteropServices;

namespace Sandbox.Animation;

public sealed class ClipBuilder
{
	private readonly List<Transform> transforms = [];
	private readonly List<Transform> rootMotion = [];
	private readonly List<(float Time, string Name)> syncEvents = [];
	private readonly List<(string Set, string Channel, float[] Values)> floatTracks = [];
	private int boneCount;

	public Skeleton Skeleton
	{
		get;
		set
		{
			if ( FrameCount > 0 && !ReferenceEquals( field, value ) )
				throw new InvalidOperationException( "The skeleton cannot change after adding frames." );
			field = value;
		}
	}
	public float FrameRate { get; set; } = 30f;
	public bool IsAdditive { get; set; }
	public bool DisableInterpolation { get; set; }
	public int FrameCount => rootMotion.Count > 0 ? rootMotion.Count : floatTracks.Count > 0 ? floatTracks[0].Values.Length : 0;

	public ClipBuilder AddFrame( ReadOnlySpan<Transform> boneTransforms ) => AddFrame( boneTransforms, Transform.Zero );

	public ClipBuilder AddFrame( ReadOnlySpan<Transform> boneTransforms, Transform motion )
	{
		if ( !Skeleton.IsValid() )
			throw new InvalidOperationException( "Set a valid skeleton before adding clip frames." );
		if ( boneTransforms.Length != Skeleton.BoneCount )
			throw new ArgumentException( "Each frame must contain one parent-local transform per skeleton bone.", nameof( boneTransforms ) );
		if ( rootMotion.Count > 0 && boneCount != Skeleton.BoneCount )
			throw new InvalidOperationException( "The skeleton's bone count changed after adding frames." );

		foreach ( var transform in boneTransforms )
			SkeletonBuilder.ValidateTransform( transform );
		SkeletonBuilder.ValidateTransform( motion );

		boneCount = boneTransforms.Length;
		foreach ( var transform in boneTransforms )
			transforms.Add( transform.WithRotation( transform.Rotation.Normal ) );
		rootMotion.Add( motion.WithRotation( motion.Rotation.Normal ) );
		return this;
	}

	/// <summary>
	/// Add one sampled value per frame for a channel declared on the skeleton. Values are
	/// copied and may be outside 0 to 1. Clips without bone frames hold the reference pose.
	/// Additive clips take channel deltas and hold identity additive bone transforms.
	/// </summary>
	public ClipBuilder AddFloatChannelTrack( string set, string channel, ReadOnlySpan<float> values )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( set );
		ArgumentException.ThrowIfNullOrWhiteSpace( channel );
		if ( !Skeleton.IsValid() )
			throw new InvalidOperationException( "Set a valid skeleton before adding float channel tracks." );
		if ( !Skeleton.native.HasFloatChannel( set, channel ) )
			throw new ArgumentException( $"The skeleton has no float channel '{channel}' in set '{set}'.", nameof( channel ) );
		if ( values.IsEmpty )
			throw new ArgumentException( "A float channel track must contain at least one sample.", nameof( values ) );
		if ( floatTracks.Any( track => string.Equals( track.Set, set, StringComparison.OrdinalIgnoreCase )
			&& string.Equals( track.Channel, channel, StringComparison.OrdinalIgnoreCase ) ) )
			throw new ArgumentException( $"Float channel '{channel}' in set '{set}' already has a track.", nameof( channel ) );
		if ( floatTracks.Count > 0 && values.Length != floatTracks[0].Values.Length )
			throw new ArgumentException( "Float channel tracks must have the same number of samples.", nameof( values ) );

		var minimum = float.MaxValue;
		var maximum = float.MinValue;
		foreach ( var value in values )
		{
			if ( !float.IsFinite( value ) )
				throw new ArgumentException( "Float channel samples must be finite.", nameof( values ) );
			minimum = MathF.Min( minimum, value );
			maximum = MathF.Max( maximum, value );
		}
		if ( !float.IsFinite( maximum - minimum ) )
			throw new ArgumentException( "The float channel's sample range is too large to encode.", nameof( values ) );

		floatTracks.Add( (set, channel, values.ToArray()) );
		return this;
	}

	/// <summary>
	/// Add one morph weight per frame. The morph must be declared with SkeletonBuilder.AddMorphChannels.
	/// </summary>
	public ClipBuilder AddMorphTrack( string name, ReadOnlySpan<float> weights ) => AddFloatChannelTrack( "MorphChannel", name, weights );

	public ClipBuilder AddSyncEvent( float time, string name = "" )
	{
		if ( !float.IsFinite( time ) || time < 0f || time >= 1f )
			throw new ArgumentOutOfRangeException( nameof( time ), "Sync event time must be normalized between zero and one, excluding one." );
		if ( syncEvents.Count > 0 && time <= syncEvents[^1].Time )
			throw new ArgumentException( "Sync events must be added in increasing time order.", nameof( time ) );

		syncEvents.Add( (time, name ?? "") );
		return this;
	}

	public unsafe Clip Create( string name = null )
	{
		ThreadSafe.AssertIsMainThread();
		if ( !Skeleton.IsValid() || FrameCount == 0 || (rootMotion.Count > 0 && boneCount != Skeleton.BoneCount) )
			throw new InvalidOperationException( "A clip must have a valid skeleton and matching frames." );
		if ( floatTracks.Any( track => track.Values.Length != FrameCount ) )
			throw new InvalidOperationException( "Each float channel track must contain one sample per clip frame." );
		if ( !float.IsFinite( FrameRate ) || FrameRate <= 0f )
			throw new InvalidOperationException( "Clip frame rate must be finite and positive." );
		if ( !float.IsFinite( (FrameCount - 1) / FrameRate ) )
			throw new InvalidOperationException( "Clip duration must be finite." );

		name = string.IsNullOrWhiteSpace( name ) ? "runtime_clip.anim" : name.Trim();
		var builder = NativeEngine.AnimationClipBuilder.Create( Skeleton.native );
		try
		{
			var frame = new Transform[Skeleton.BoneCount + Skeleton.BoneOffset];
			if ( Skeleton.BoneOffset > 0 )
				frame[0] = IsAdditive ? Transform.Zero.WithScale( 0f ) : Transform.Zero;

			if ( rootMotion.Count == 0 )
			{
				foreach ( var bone in Skeleton.Bones )
					frame[bone.Index + Skeleton.BoneOffset] = IsAdditive ? Transform.Zero.WithScale( 0f ) : bone.LocalTransform;
			}
			for ( var i = 0; i < FrameCount; ++i )
			{
				if ( rootMotion.Count > 0 )
					CollectionsMarshal.AsSpan( transforms ).Slice( i * boneCount, boneCount ).CopyTo( frame.AsSpan( Skeleton.BoneOffset ) );
				fixed ( Transform* data = frame )
				{
					if ( !builder.AddFrame( (IntPtr)data, frame.Length, rootMotion.Count > 0 ? rootMotion[i] : Transform.Zero ) )
						throw new InvalidOperationException( $"Failed to add frame {i} to animation clip '{name}'." );
				}
			}
			foreach ( var track in floatTracks )
			{
				fixed ( float* data = track.Values )
				{
					if ( !builder.AddFloatChannelTrack( track.Set, track.Channel, (IntPtr)data, track.Values.Length ) )
						throw new InvalidOperationException( $"Failed to add float channel '{track.Channel}' in set '{track.Set}' to animation clip '{name}'." );
				}
			}
			foreach ( var marker in syncEvents )
				builder.AddSyncEvent( marker.Time, marker.Name );

			var clip = Clip.FromNative( builder.CreateResource( name.Replace( ':', '_' ), Skeleton.ResourcePath, FrameRate, IsAdditive, DisableInterpolation ), name, procedural: true );
			return clip ?? throw new InvalidOperationException( $"Failed to create animation clip '{name}'." );
		}
		finally
		{
			builder.DeleteThis();
		}
	}
}
