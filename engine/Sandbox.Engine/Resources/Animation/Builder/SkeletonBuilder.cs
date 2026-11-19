namespace Sandbox.Animation;

public sealed class SkeletonBuilder
{
	private readonly List<(string Name, int Parent, Transform Transform)> bones = [];
	private readonly Dictionary<string, int> boneIndices = new( StringComparer.OrdinalIgnoreCase );
	private readonly List<(string Name, string[] Channels)> channelSets = [];
	private readonly HashSet<string> channelNames = new( StringComparer.OrdinalIgnoreCase );
	private readonly List<(string Name, Dictionary<string, float> Weights, float FloatChannelWeight)> masks = [];

	public int BoneCount => bones.Count;
	public int LowLodBoneCount { get; set; }
	public bool IsProp { get; set; }

	public SkeletonBuilder AddBone( string name, Transform localTransform, string parent = null )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( name );
		if ( boneIndices.ContainsKey( name ) || channelNames.Contains( name ) )
			throw new ArgumentException( $"A bone or float channel named '{name}' already exists.", nameof( name ) );

		var parentIndex = -1;
		if ( parent is not null && !boneIndices.TryGetValue( parent, out parentIndex ) )
			throw new ArgumentException( $"Parent bone '{parent}' must be added first.", nameof( parent ) );
		ValidateTransform( localTransform );
		if ( localTransform.Scale.x == 0f )
			throw new ArgumentException( "Skeleton reference transforms must have nonzero scale.", nameof( localTransform ) );
		boneIndices.Add( name, bones.Count );
		bones.Add( (name, parentIndex, localTransform.WithRotation( localTransform.Rotation.Normal )) );
		return this;
	}

	/// <summary>
	/// Add a named set of float channels sampled and blended alongside the bone transforms.
	/// Channel names must be unique across all bones and float-channel sets, ignoring case.
	/// </summary>
	public SkeletonBuilder AddFloatChannelSet( string name, params string[] channels )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( name );
		ArgumentNullException.ThrowIfNull( channels );
		if ( channelSets.Any( set => string.Equals( set.Name, name, StringComparison.OrdinalIgnoreCase ) ) )
			throw new ArgumentException( $"A float channel set named '{name}' already exists.", nameof( name ) );
		if ( channels.Length == 0 )
			throw new ArgumentException( "A float channel set must contain at least one channel.", nameof( channels ) );

		var names = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		foreach ( var channel in channels )
		{
			ArgumentException.ThrowIfNullOrWhiteSpace( channel );
			if ( boneIndices.ContainsKey( channel ) || channelNames.Contains( channel ) )
				throw new ArgumentException( $"A bone or float channel named '{channel}' already exists.", nameof( channels ) );
			if ( !names.Add( channel ) )
				throw new ArgumentException( $"Float channel '{channel}' appears more than once in the set.", nameof( channels ) );
		}

		channelSets.Add( (name, channels.ToArray()) );
		channelNames.UnionWith( names );
		return this;
	}

	/// <summary>
	/// Add morph channels that drive the model's blend shapes with matching names.
	/// </summary>
	public SkeletonBuilder AddMorphChannels( params string[] names ) => AddFloatChannelSet( "MorphChannel", names );

	/// <summary>
	/// Add a named mask of bone and float-channel weights. Unspecified bones feather between
	/// authored weights or inherit their nearest weighted ancestor. Unspecified float channels
	/// use <paramref name="defaultFloatChannelWeight"/>.
	/// </summary>
	public SkeletonBuilder AddBoneMask( string name, IReadOnlyDictionary<string, float> boneWeights, float defaultFloatChannelWeight = 1f )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( name );
		ArgumentNullException.ThrowIfNull( boneWeights );
		if ( masks.Any( mask => string.Equals( mask.Name, name, StringComparison.OrdinalIgnoreCase ) ) )
			throw new ArgumentException( $"A bone mask named '{name}' already exists.", nameof( name ) );
		if ( boneWeights.Count == 0 )
			throw new ArgumentException( "A bone mask must define at least one bone weight.", nameof( boneWeights ) );
		if ( !float.IsFinite( defaultFloatChannelWeight ) || defaultFloatChannelWeight is < 0f or > 1f )
			throw new ArgumentOutOfRangeException( nameof( defaultFloatChannelWeight ) );

		var entries = new Dictionary<string, float>( boneWeights.Count, StringComparer.OrdinalIgnoreCase );
		foreach ( var (bone, weight) in boneWeights )
		{
			if ( string.IsNullOrWhiteSpace( bone ) || (!boneIndices.ContainsKey( bone ) && !channelNames.Contains( bone )) )
				throw new ArgumentException( $"Bone or float channel '{bone}' must be added before its mask.", nameof( boneWeights ) );
			if ( !float.IsFinite( weight ) || weight is < 0f or > 1f )
				throw new ArgumentOutOfRangeException( nameof( boneWeights ), $"Weight for bone '{bone}' must be finite and between zero and one." );
			if ( !entries.TryAdd( bone, weight ) )
				throw new ArgumentException( $"Bone or float channel '{bone}' appears more than once in the mask.", nameof( boneWeights ) );
		}

		masks.Add( (name, entries, defaultFloatChannelWeight) );
		return this;
	}

	internal static void ValidateTransform( Transform transform )
	{
		var rotation = transform.Rotation;
		var scale = transform.Scale;
		if ( !transform.Position.IsFinite || !rotation.IsFinite || !scale.IsFinite )
			throw new ArgumentException( "Animation transforms must be finite.", nameof( transform ) );
		if ( scale.x != scale.y || scale.x != scale.z )
			throw new ArgumentException( "Animation transforms must have uniform scale.", nameof( transform ) );

		var lengthSquared = rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z + rotation.w * rotation.w;
		if ( !float.IsFinite( lengthSquared ) || lengthSquared <= 0f )
			throw new ArgumentException( "Animation transforms must have a valid rotation.", nameof( transform ) );
	}

	public unsafe Skeleton Create( string name = null )
	{
		ThreadSafe.AssertIsMainThread();
		if ( bones.Count == 0 )
			throw new InvalidOperationException( "A skeleton must contain at least one bone." );
		if ( LowLodBoneCount < 0 || LowLodBoneCount > bones.Count )
			throw new InvalidOperationException( "Low LOD bone count must be between zero and the total bone count." );

		name = string.IsNullOrWhiteSpace( name ) ? "runtime_skeleton.skel" : name.Trim();
		var builder = NativeEngine.AnimationSkeletonBuilder.Create();
		try
		{
			foreach ( var bone in bones )
				builder.AddBone( bone.Name, bone.Parent, bone.Transform );

			foreach ( var set in channelSets )
			{
				var index = builder.AddFloatChannelSet( set.Name );
				if ( index < 0 )
					throw new InvalidOperationException( $"Failed to add float channel set '{set.Name}' to skeleton '{name}'." );
				foreach ( var channel in set.Channels )
				{
					if ( !builder.AddFloatChannel( index, channel ) )
						throw new InvalidOperationException( $"Failed to add float channel '{channel}' to skeleton '{name}'." );
				}
			}

			var weightNames = bones.Select( bone => bone.Name ).Concat( channelSets.SelectMany( set => set.Channels ) ).ToArray();
			foreach ( var mask in masks )
			{
				var weights = new float[weightNames.Length];
				for ( var i = 0; i < weights.Length; i++ )
					weights[i] = mask.Weights.TryGetValue( weightNames[i], out var weight ) ? weight : -1f;
				fixed ( float* data = weights )
				{
					if ( !builder.AddBoneMask( mask.Name, (IntPtr)data, weights.Length, mask.FloatChannelWeight ) )
						throw new InvalidOperationException( $"Failed to add bone mask '{mask.Name}' to skeleton '{name}'." );
				}
			}

			var skeleton = Skeleton.FromNative( builder.CreateResource( name.Replace( ':', '_' ), LowLodBoneCount == 0 ? bones.Count : LowLodBoneCount, IsProp ), name, procedural: true );
			return skeleton ?? throw new InvalidOperationException( $"Failed to create animation skeleton '{name}'." );
		}
		finally
		{
			builder.DeleteThis();
		}
	}
}
