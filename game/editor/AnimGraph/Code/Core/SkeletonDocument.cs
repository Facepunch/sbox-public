using System.Text.Json.Nodes;

namespace Editor.AnimGraph;

/// <summary>
/// The authored half of a skeleton - the .skel document rather than the compiled resource. The
/// bone hierarchy comes from the source art, so what lives here is everything the compiler can't
/// work out on its own.
/// </summary>
public sealed class SkeletonDocument : IAnimationDocument
{
	public sealed class FloatChannelSet
	{
		public string Name { get; set; } = "";
		public List<string> Channels { get; set; } = [];
	}

	/// <summary>
	/// One bone's weight in a mask. Unweighted chains feather between authored weights, with
	/// terminal chains inheriting their nearest authored ancestor.
	/// </summary>
	public sealed class BoneWeight
	{
		public string Bone { get; set; }

		[Range( 0, 1 )]
		public float Weight { get; set; } = 1.0f;
	}

	/// <summary>
	/// A named weight per bone, which an animation graph blends a pose through.
	/// </summary>
	public sealed class BoneMask
	{
		public string Name { get; set; }
		public List<BoneWeight> Weights { get; set; } = new();

		[Property, Range( 0, 1 )]
		public float DefaultFloatChannelWeight { get; set; } = 1.0f;

		[Property, WideMode]
		public List<FloatChannelWeight> FloatChannelWeights { get; set; } = [];
	}

	public sealed class FloatChannelWeight
	{
		public string Channel { get; set; }

		[Range( 0, 1 )]
		public float Weight { get; set; } = 1.0f;
	}

	/// <summary>
	/// Absolute path of the .skel this was read from, and what <see cref="Save"/> writes back to.
	/// </summary>
	public string FilePath { get; private init; }

	/// <summary>
	/// The compiled bone hierarchy used by the inspector. Not part of the authored document.
	/// </summary>
	[Hide]
	public Skeleton SourceSkeleton { get; set; }

	[Property, Hide, ResourceType( "vmdl" ), Description( "Optional model used to preview this skeleton and its animation clips in the editor." )]
	public string PreviewModel { get; set; } = "";

	[Property, WideMode, FilePath( Extension = "fbx,dmx,smd" ), Title( "Additional Skeleton Files" )]
	public List<string> AdditionalSourceFilenames { get; set; } = [];

	/// <summary>
	/// The bone to use as the skeleton's root, or empty for the whole skeleton. Naming a bone part
	/// way down a hierarchy compiles only that part.
	/// </summary>
	[Property, WideMode, Editor( "animgraph.bone" )]
	public string RootBone { get; set; }

	/// <summary>
	/// What the source art is scaled by on import, matching the model's import scale. A model built
	/// at a different size to its art needs the same scale here, or animations drive it at the
	/// wrong size.
	/// </summary>
	[Property, WideMode, Editor( "importscale" )]
	public float Scale
	{
		get => _scale;
		// Zero collapses the rig onto a point and negatives turn it inside out
		set => _scale = MathF.Max( value, MinimumScale );
	}

	private const float MinimumScale = 0.0001f;
	private float _scale = 1.0f;

	/// <summary>
	/// Named weights an animation graph can blend a pose through.
	/// </summary>
	public List<BoneMask> Masks { get; set; } = new();

	/// <summary>
	/// Bones only worth sampling up close. Naming a bone covers everything under it, and the graph
	/// stops evaluating them entirely once the model drops off its top LOD.
	/// </summary>
	[Property, WideMode, Editor( "animgraph.bone" ), Title( "High LOD Bones" )]
	public List<string> HighLODBones { get; set; } = new();

	public List<FloatChannelSet> FloatChannelSets { get; set; } = [];

	/// <summary>
	/// Read a .skel. Null if it isn't there or doesn't parse.
	/// </summary>
	public static SkeletonDocument Load( string filePath )
	{
		if ( Kv3Document.Read( filePath ) is not { } document )
			return null;

		try
		{
			return new SkeletonDocument
			{
				FilePath = filePath,
				PreviewModel = (string)document[PreviewModelKey] ?? "",
				RootBone = (string)document[RootBoneKey],
				AdditionalSourceFilenames = (document[AdditionalSourcesKey] as JsonArray)?.Select( x => x.GetValue<string>() ).ToList() ?? [],
				Scale = ReadScale( document ),
				Masks = ReadMasks( document ),
				HighLODBones = ReadHighLODBones( document ),
				FloatChannelSets = ReadFloatChannelSets( document )
			};
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Couldn't read {filePath}" );
			return null;
		}
	}

	/// <summary>
	/// Write back what we author. Everything else in the document is left as it was found, so
	/// members this doesn't know about survive a round trip.
	/// </summary>
	public bool Save()
	{
		if ( Kv3Document.Read( FilePath ) is not { } document )
		{
			Log.Warning( $"Couldn't write {FilePath} - it's gone or no longer reads as a skeleton" );
			return false;
		}

		document[RootBoneKey] = RootBone ?? string.Empty;
		if ( string.IsNullOrWhiteSpace( PreviewModel ) )
			document.Remove( PreviewModelKey );
		else
			document[PreviewModelKey] = PreviewModel;
		document[AdditionalSourcesKey] = new JsonArray( AdditionalSourceFilenames.Select( x => JsonValue.Create( x ) ).ToArray<JsonNode>() );
		document[ScaleKey] = Scale;
		document[MasksKey] = WriteMasks();
		document[HighLODBonesKey] = WriteHighLODBones();
		document[FloatChannelSetsKey] = WriteFloatChannelSets();

		return Kv3Document.Write( FilePath, document );
	}

	/// <summary>
	/// Everything this document authors, as text an undo stack can hold onto.
	/// </summary>
	public string Snapshot() => Json.Serialize( new State { RootBone = RootBone, PreviewModel = PreviewModel, AdditionalSourceFilenames = AdditionalSourceFilenames, Scale = Scale, Masks = Masks, HighLODBones = HighLODBones, FloatChannelSets = FloatChannelSets } );

	/// <summary>
	/// Put a <see cref="Snapshot"/> back. The masks are rebuilt from it, so anything holding one
	/// from before is stale and has to be looked up again.
	/// </summary>
	public void Restore( string snapshot )
	{
		try
		{
			var state = Json.Deserialize<State>( snapshot );

			RootBone = state?.RootBone;
			PreviewModel = state?.PreviewModel ?? "";
			AdditionalSourceFilenames = state?.AdditionalSourceFilenames ?? [];
			Scale = state?.Scale ?? 1.0f;
			Masks = state?.Masks ?? new List<BoneMask>();
			HighLODBones = state?.HighLODBones ?? new List<string>();
			FloatChannelSets = state?.FloatChannelSets ?? [];
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Couldn't restore {FilePath}" );
		}
	}

	private sealed class State
	{
		public string RootBone { get; set; }
		public string PreviewModel { get; set; }
		public List<string> AdditionalSourceFilenames { get; set; } = [];
		public float Scale { get; set; } = 1.0f;
		public List<BoneMask> Masks { get; set; }
		public List<string> HighLODBones { get; set; }
		public List<FloatChannelSet> FloatChannelSets { get; set; }
	}

	private const string RootBoneKey = "m_rootBoneName";
	private const string PreviewModelKey = "m_previewModel";
	private const string AdditionalSourcesKey = "m_additionalSourceFilenames";
	private const string ScaleKey = "m_flGlobalScale";
	private const string MasksKey = "m_boneMaskSetDefinitions";
	private const string HighLODBonesKey = "m_highLODBones";
	private const string FloatChannelSetsKey = "m_floatChannelSets";
	private const string MaskNameKey = "m_ID";
	private const string WeightListKey = "m_primaryWeightList";
	private const string BonesKey = "m_boneIDs";
	private const string WeightsKey = "m_weights";
	private const string DefaultFloatChannelWeightKey = "m_defaultFloatChannelWeight";


	// A missing or nonsense scale reads as unscaled
	private static float ReadScale( JsonObject document )
	{
		var scale = (float?)document[ScaleKey] ?? 1.0f;

		return scale >= MinimumScale ? scale : 1.0f;
	}

	private static List<BoneMask> ReadMasks( JsonObject document )
	{
		var masks = new List<BoneMask>();

		if ( document[MasksKey] is not JsonArray array )
			return masks;

		var channels = ReadFloatChannelSets( document ).SelectMany( set => set.Channels ).ToHashSet( StringComparer.OrdinalIgnoreCase );
		foreach ( var element in array.OfType<JsonObject>() )
		{
			var mask = new BoneMask { Name = (string)element[MaskNameKey] };
			masks.Add( mask );

			if ( element[WeightListKey] is not JsonObject list || list[BonesKey] is not JsonArray bones )
				continue;

			var weights = list[WeightsKey] as JsonArray;
			mask.DefaultFloatChannelWeight = (float?)list[DefaultFloatChannelWeightKey] ?? 1.0f;

			for ( var i = 0; i < bones.Count; i++ )
			{
				var id = (string)bones[i];
				var weight = i < weights?.Count ? (float)weights[i] : 1.0f;
				if ( channels.Contains( id ) )
				{
					mask.FloatChannelWeights.Add( new() { Channel = id, Weight = weight } );
					continue;
				}
				mask.Weights.Add( new BoneWeight
				{
					Bone = id,
					Weight = weight
				} );
			}
		}

		return masks;
	}

	private JsonArray WriteMasks()
	{
		var array = new JsonArray();

		foreach ( var mask in Masks )
		{
			// A mask with no name or nothing weighted isn't something the compiler can build, and
			// writing one would fail the whole skeleton - leave it out until it's filled in
			var weights = mask.Weights?.Where( x => !string.IsNullOrWhiteSpace( x.Bone ) ).ToArray() ?? [];
			var channels = mask.FloatChannelWeights.Where( x => !string.IsNullOrWhiteSpace( x.Channel ) ).ToArray();
			if ( string.IsNullOrWhiteSpace( mask.Name ) || weights.Length + channels.Length == 0 )
				continue;

			array.Add( new JsonObject
			{
				[MaskNameKey] = mask.Name,
				[WeightListKey] = new JsonObject
				{
					[BonesKey] = new JsonArray( weights.Select( x => x.Bone ).Concat( channels.Select( x => x.Channel ) ).Select( x => JsonValue.Create( x ) ).ToArray<JsonNode>() ),
					[WeightsKey] = new JsonArray( weights.Select( x => x.Weight ).Concat( channels.Select( x => x.Weight ) ).Select( x => JsonValue.Create( x ) ).ToArray<JsonNode>() ),
					[DefaultFloatChannelWeightKey] = mask.DefaultFloatChannelWeight
				}
			} );
		}

		return array;
	}

	private static List<string> ReadHighLODBones( JsonObject document )
	{
		if ( document[HighLODBonesKey] is not JsonArray array )
			return new List<string>();

		return array.Select( x => (string)x ).Where( x => !string.IsNullOrWhiteSpace( x ) ).ToList();
	}

	private static List<FloatChannelSet> ReadFloatChannelSets( JsonObject document )
	{
		if ( document[FloatChannelSetsKey] is not JsonArray sets )
			return [];

		return sets.Select( set => new FloatChannelSet
		{
			Name = (string)set["m_ID"],
			Channels = set["m_channelIDs"].AsArray().Select( channel => channel.GetValue<string>() ).ToList()
		} ).ToList();
	}

	private JsonArray WriteFloatChannelSets() => new( FloatChannelSets.Select( set => new JsonObject
	{
		["m_ID"] = set.Name,
		["m_channelIDs"] = new JsonArray( set.Channels.Select( channel => JsonValue.Create( channel ) ).ToArray<JsonNode>() )
	} ).ToArray<JsonNode>() );

	private JsonArray WriteHighLODBones()
	{
		var bones = HighLODBones?.Where( x => !string.IsNullOrWhiteSpace( x ) ).Distinct() ?? [];

		return new JsonArray( bones.Select( x => JsonValue.Create( x ) ).ToArray<JsonNode>() );
	}
}
