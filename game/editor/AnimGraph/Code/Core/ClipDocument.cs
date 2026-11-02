using System.Text.Json.Nodes;

namespace Editor.AnimGraph;

/// <summary>
/// The authored half of an animation clip - the .anim document rather than the compiled resource.
/// The pose data comes from the source art; this authors import settings and event markup.
/// </summary>
public sealed class ClipDocument
{
	[Property, Group( "Import" ), Title( "Animation File" ), FilePath( Extension = "fbx,dmx,smd" ), Description( "Choose a project animation, drag in a file, or browse for an FBX, DMX or SMD file on your computer." ), HideIf( nameof( UseMorphFrame ), true )]
	public string SourceFilename
	{
		get => ReadString( SourceFilenameKey );
		set => _root[SourceFilenameKey] = value ?? "";
	}

	[Property, Group( "Import" ), Title( "Skeleton" ), ResourceType( "skel" )]
	public string SkeletonName
	{
		get => ReadString( SkeletonKey );
		set => _root[SkeletonKey] = AssetSystem.FindByPath( value )?.Path ?? value ?? "";
	}

	[Property, Group( "Import Settings", StartFolded = true ), Description( "Which animation to import when the source file contains several takes. 0 selects the first take." )]
	public int Take
	{
		get => ReadInt( TakeKey, 0 );
		set => _root[TakeKey] = Math.Max( value, 0 );
	}

	[Property, Group( "Import Settings", StartFolded = true ), Title( "Start Frame" ), Description( "Override the first source frame. Leave unset to use the beginning of the source animation." )]
	public int? StartFrame
	{
		get => ReadInt( StartFrameKey, -1 ) is var frame && frame >= 0 ? frame : null;
		set => _root[StartFrameKey] = Math.Max( value ?? -1, -1 );
	}

	[Property, Group( "Import Settings", StartFolded = true ), Title( "End Frame" ), Description( "Override the last source frame. Leave unset to use the end of the source animation." )]
	public int? EndFrame
	{
		get => ReadInt( EndFrameKey, -1 ) is var frame && frame >= 0 ? frame : null;
		set => _root[EndFrameKey] = Math.Max( value ?? -1, -1 );
	}

	[Property, Group( "Import Settings", StartFolded = true ), Title( "Import Scale" ), Description( "Extra scale applied after the skeleton's import scale. Leave at 1 when the animation and original rig use the same source units." )]
	public float ImportScale
	{
		get => _root[ImportScaleKey] is JsonValue value && value.TryGetValue( out float scale ) ? scale : 1f;
		set => _root[ImportScaleKey] = value;
	}

	[Property, Group( "Import Settings", StartFolded = true ), Title( "Frame Rate" ), DefaultValue( 30f ), Description( "Override frames per second. Leave unset to use the source frame rate." )]
	public float? ImportFrameRate
	{
		get => ReadFloat( ImportFrameRateKey, -1f ) is var rate && rate >= 0 ? rate : null;
		set => _root[ImportFrameRateKey] = value ?? -1f;
	}

	[Property, Group( "Import Settings", StartFolded = true ), Title( "Duration" ), DefaultValue( 1f ), Description( "Override duration in seconds. Leave unset to use the source duration. Single-frame poses are unaffected." )]
	public float? DurationOverride
	{
		get => ReadFloat( DurationOverrideKey, -1f ) is var duration && duration >= 0 ? duration : null;
		set => _root[DurationOverrideKey] = value ?? -1f;
	}

	[Property, Group( "Loop Smoothing", StartFolded = true ), Title( "Enabled" )]
	public bool SmoothLoop
	{
		get => ReadBool( SmoothLoopKey, false );
		set => _root[SmoothLoopKey] = value;
	}

	[Property, Group( "Loop Smoothing", StartFolded = true ), Title( "Frames From Start" ), ShowIf( nameof( SmoothLoop ), true )]
	public int SmoothLoopFramesFromStart
	{
		get => ReadInt( SmoothLoopFramesFromStartKey, 10 );
		set => _root[SmoothLoopFramesFromStartKey] = Math.Max( value, 0 );
	}

	[Property, Group( "Loop Smoothing", StartFolded = true ), Title( "Frames From End" ), ShowIf( nameof( SmoothLoop ), true )]
	public int SmoothLoopFramesFromEnd
	{
		get => ReadInt( SmoothLoopFramesFromEndKey, 10 );
		set => _root[SmoothLoopFramesFromEndKey] = Math.Max( value, 0 );
	}

	[Property, Group( "Loop Smoothing", StartFolded = true ), Title( "Start Loop Frame" )]
	public int StartLoopFrame
	{
		get => ReadInt( StartLoopFrameKey, 0 );
		set => _root[StartLoopFrameKey] = Math.Max( value, 0 );
	}

	[Property, Group( "Additive" ), Title( "Linear Delta" )]
	public bool LinearDelta
	{
		get => ReadBool( LinearDeltaKey, false );
		set => _root[LinearDeltaKey] = value;
	}

	[Property, Group( "Additive" ), Title( "Spline" ), ShowIf( nameof( LinearDelta ), true )]
	public bool LinearDeltaSpline
	{
		get => ReadBool( LinearDeltaSplineKey, false );
		set => _root[LinearDeltaSplineKey] = value;
	}

	public sealed class MorphWeight
	{
		public string Morph { get; set; } = "";
		[Range( 0, 1 )]
		public float Weight { get; set; }
	}

	[Property, Group( "Morph Frame" ), Title( "Use Morph Frame" )]
	public bool UseMorphFrame
	{
		get => _root[MorphFrameWeightsKey] is not null;
		set
		{
			if ( value )
				_root[MorphFrameWeightsKey] ??= new JsonArray();
			else
				_root.Remove( MorphFrameWeightsKey );
			_morphFrameWeights = null;
		}
	}

	[Property, Group( "Morph Frame" ), WideMode, Title( "Weights" ), Editor( "animgraph.morph-weights" ), ShowIf( nameof( UseMorphFrame ), true )]
	public List<MorphWeight> MorphFrameWeights
	{
		get => _morphFrameWeights ??= (_root[MorphFrameWeightsKey] as JsonArray ?? []).Select( weight => new MorphWeight
		{
			Morph = weight["morph"].GetValue<string>(),
			Weight = weight["weight"].GetValue<float>()
		} ).ToList();
	}

	private void UpdateMorphFrameWeights()
	{
		if ( _morphFrameWeights is null || !UseMorphFrame )
			return;

		_root[MorphFrameWeightsKey] = new JsonArray( _morphFrameWeights.Select( weight => new JsonObject
		{
			["morph"] = weight.Morph,
			["weight"] = weight.Weight
		} ).ToArray<JsonNode>() );
	}

	[Flags]
	public enum Axes
	{
		None = 0,
		X = 1,
		Y = 2,
		Z = 4
	}

	public enum FrameMode
	{
		Number,
		[Title( "First Frame" )] FirstFrame,
		[Title( "Last Frame" )] LastFrame
	}

	[Property, Group( "Alignment", StartFolded = true ), Title( "Reference Animation" ), FilePath( Extension = "fbx,dmx,smd" ), Description( "Source animation to align against before extracting motion. Clear this to disable alignment." )]
	public string AlignmentReference
	{
		get => ReadString( AlignmentReferenceKey );
		set => _root[AlignmentReferenceKey] = value ?? "";
	}

	[Property, Group( "Alignment", StartFolded = true ), Title( "Bone" ), Editor( "animgraph.bone" ), ShowIf( nameof( HasAlignment ), true )]
	public string AlignmentBone
	{
		get => ReadString( AlignmentBoneKey );
		set => _root[AlignmentBoneKey] = value ?? "";
	}

	[Property, Group( "Alignment", StartFolded = true ), Title( "Source Frame" ), ShowIf( nameof( HasAlignment ), true )]
	public FrameMode AlignmentFrameMode
	{
		get => ReadEnum( AlignmentFrameModeKey, FrameMode.Number );
		set => _root[AlignmentFrameModeKey] = value.ToString();
	}

	[Property, Group( "Alignment", StartFolded = true ), Title( "Source Frame Number" ), ShowIf( nameof( HasAlignmentFrame ), true )]
	public int AlignmentFrame
	{
		get => ReadInt( AlignmentFrameKey, 0 );
		set => _root[AlignmentFrameKey] = Math.Max( value, 0 );
	}

	[Property, Group( "Alignment", StartFolded = true ), Title( "Reference Frame" ), ShowIf( nameof( HasAlignment ), true )]
	public FrameMode AlignmentReferenceFrameMode
	{
		get => ReadEnum( AlignmentReferenceFrameModeKey, FrameMode.Number );
		set => _root[AlignmentReferenceFrameModeKey] = value.ToString();
	}

	[Property, Group( "Alignment", StartFolded = true ), Title( "Reference Frame Number" ), ShowIf( nameof( HasAlignmentReferenceFrame ), true )]
	public int AlignmentReferenceFrame
	{
		get => ReadInt( AlignmentReferenceFrameKey, 0 );
		set => _root[AlignmentReferenceFrameKey] = Math.Max( value, 0 );
	}

	[Property, Group( "Alignment", StartFolded = true ), Title( "Translation Axes" ), ShowIf( nameof( HasAlignment ), true )]
	public Axes AlignmentTranslation
	{
		get => ReadAxes( "m_bAlignT", Axes.X | Axes.Y );
		set => WriteAxes( "m_bAlignT", value );
	}

	[Property, Group( "Alignment", StartFolded = true ), Title( "Rotation Axes" ), ShowIf( nameof( HasAlignment ), true )]
	public Axes AlignmentRotation
	{
		get => ReadAxes( "m_bAlignR", Axes.None );
		set => WriteAxes( "m_bAlignR", value );
	}

	public enum MotionExtractionMode
	{
		Uniform,
		Single,
		Specified
	}

	[Property, Group( "Motion Extraction", StartFolded = true ), Title( "Extract Motion" )]
	public bool ExtractMotion
	{
		get => ReadBool( ExtractMotionKey, false );
		set => _root[ExtractMotionKey] = value;
	}

	[Property, Group( "Motion Extraction", StartFolded = true ), Title( "Bone" ), Editor( "animgraph.bone" ), ShowIf( nameof( ExtractMotion ), true )]
	public string MotionExtractionBone
	{
		get => ReadString( MotionExtractionBoneKey );
		set => _root[MotionExtractionBoneKey] = value ?? "";
	}

	[Property, Group( "Motion Extraction", StartFolded = true ), Title( "Translation Axes" ), ShowIf( nameof( ExtractMotion ), true )]
	public Axes MotionExtractionTranslation
	{
		get => ReadAxes( "m_bExtractMotionT", Axes.X | Axes.Y );
		set => WriteAxes( "m_bExtractMotionT", value );
	}

	[Property, Group( "Motion Extraction", StartFolded = true ), Title( "Extract Yaw" ), ShowIf( nameof( ExtractMotion ), true )]
	public bool MotionExtractionYaw
	{
		get => ReadBool( MotionExtractionYawKey, false );
		set => _root[MotionExtractionYawKey] = value;
	}

	[Property, Group( "Motion Extraction", StartFolded = true ), Title( "Linear" ), ShowIf( nameof( ExtractMotion ), true )]
	public bool MotionExtractionLinear
	{
		get => ReadBool( MotionExtractionLinearKey, false );
		set => _root[MotionExtractionLinearKey] = value;
	}

	[Property, Group( "Motion Extraction", StartFolded = true ), Title( "Quadratic" ), ShowIf( nameof( ExtractMotion ), true )]
	public bool MotionExtractionQuadratic
	{
		get => ReadBool( MotionExtractionQuadraticKey, false );
		set => _root[MotionExtractionQuadraticKey] = value;
	}

	[Property, Group( "Motion Extraction", StartFolded = true ), Title( "Mode" ), ShowIf( nameof( ExtractMotion ), true )]
	public MotionExtractionMode MotionExtraction
	{
		get => ReadEnum( MotionExtractionModeKey, MotionExtractionMode.Uniform );
		set => _root[MotionExtractionModeKey] = value.ToString();
	}

	[Property, Group( "Motion Extraction", StartFolded = true ), Title( "Frames" ), ShowIf( nameof( HasMotionExtractionFrames ), true )]
	public List<int> MotionExtractionFrames
	{
		get => _motionExtractionFrames ??= ReadMotionExtractionFrames();
		set => _motionExtractionFrames = value ?? [];
	}

	[Property, Group( "Animation Offsets", StartFolded = true ), Title( "Pre-Extraction Translation" ), Description( "ModelDoc animation offset in engine units, applied after alignment and before motion extraction." )]
	public Vector3 PreExtractionTranslation
	{
		get => ReadVector( PreExtractionTranslationKey );
		set => WriteVector( PreExtractionTranslationKey, value );
	}

	[Property, Group( "Animation Offsets", StartFolded = true ), Title( "Pre-Extraction Rotation" ), Description( "ModelDoc Euler XYZ rotation in degrees, applied after alignment and before motion extraction." )]
	public Vector3 PreExtractionRotation
	{
		get => ReadVector( PreExtractionRotationKey );
		set => WriteVector( PreExtractionRotationKey, value );
	}

	[Property, Group( "Animation Offsets", StartFolded = true ), Title( "Post-Extraction Translation" ), Description( "ModelDoc animation offset in engine units, applied after motion extraction." )]
	public Vector3 PostExtractionTranslation
	{
		get => ReadVector( PostExtractionTranslationKey );
		set => WriteVector( PostExtractionTranslationKey, value );
	}

	[Property, Group( "Animation Offsets", StartFolded = true ), Title( "Post-Extraction Rotation" ), Description( "ModelDoc Euler XYZ rotation in degrees, applied after motion extraction." )]
	public Vector3 PostExtractionRotation
	{
		get => ReadVector( PostExtractionRotationKey );
		set => WriteVector( PostExtractionRotationKey, value );
	}

	internal Skeleton SourceSkeleton => string.IsNullOrWhiteSpace( SkeletonName ) ? null : Skeleton.Load( SkeletonName );
	private string _channelSkeleton;
	private IReadOnlyList<string> _sourceFloatChannels = [];

	internal IReadOnlyList<string> SourceFloatChannels
	{
		get
		{
			if ( _channelSkeleton == SkeletonName )
				return _sourceFloatChannels;

			_channelSkeleton = SkeletonName;
			var asset = AssetSystem.FindByPath( SkeletonName );
			var skeleton = asset?.HasSourceFile == true ? SkeletonDocument.Load( asset.GetSourceFile( true ) ) : null;
			_sourceFloatChannels = skeleton?.FloatChannelSets.SelectMany( set => set.Channels ).Distinct( StringComparer.OrdinalIgnoreCase ).Order( StringComparer.OrdinalIgnoreCase ).ToArray() ?? [];
			return _sourceFloatChannels;
		}
	}

	private bool HasAlignment => !string.IsNullOrWhiteSpace( AlignmentReference );
	private bool HasAlignmentFrame => HasAlignment && AlignmentFrameMode == FrameMode.Number;
	private bool HasAlignmentReferenceFrame => HasAlignment && AlignmentReferenceFrameMode == FrameMode.Number;
	private bool HasMotionExtractionFrames => ExtractMotion && MotionExtraction == MotionExtractionMode.Specified;

	public enum AdditiveMode
	{
		None,
		RelativeToSkeleton,
		RelativeToFrame,
		RelativeToClip
	}

	[Property, Group( "Additive" ), WideMode, Title( "Additive Type" )]
	public AdditiveMode AdditiveType
	{
		get => Enum.TryParse<AdditiveMode>( ReadString( AdditiveTypeKey ), out var mode ) ? mode : AdditiveMode.None;
		set => _root[AdditiveTypeKey] = value.ToString();
	}

	[Property, Group( "Additive" ), Title( "Model Space" ), ShowIf( nameof( HasAdditive ), true )]
	public bool AdditiveModelSpace
	{
		get => ReadBool( AdditiveModelSpaceKey, false );
		set => _root[AdditiveModelSpaceKey] = value;
	}

	/// <summary>
	/// Source animation file to subtract, not a compiled .anim clip.
	/// </summary>
	[Property, Group( "Additive" ), WideMode, Title( "Base Animation" ), FilePath( Extension = "fbx,dmx" ), ShowIf( nameof( AdditiveType ), AdditiveMode.RelativeToClip )]
	public string AdditiveBaseFile
	{
		get => ReadString( AdditiveBaseFileKey );
		set => _root[AdditiveBaseFileKey] = value ?? string.Empty;
	}

	/// <summary>
	/// Zero-based take index in the base source file.
	/// </summary>
	[Property, Group( "Additive" ), WideMode, Title( "Base Take" ), ShowIf( nameof( AdditiveType ), AdditiveMode.RelativeToClip )]
	public int AdditiveBaseTake
	{
		get => ReadInt( AdditiveBaseTakeKey, 0 );
		set => _root[AdditiveBaseTakeKey] = Math.Max( value, 0 );
	}

	/// <summary>
	/// First source frame of the base animation. Leave unset to use the beginning of the take.
	/// </summary>
	[Property, Group( "Additive" ), WideMode, Title( "Trim Start" ), ShowIf( nameof( AdditiveType ), AdditiveMode.RelativeToClip )]
	public int? AdditiveBaseStartFrame
	{
		get => ReadInt( AdditiveBaseStartFrameKey, -1 ) is var frame && frame >= 0 ? frame : null;
		set => _root[AdditiveBaseStartFrameKey] = Math.Max( value ?? -1, -1 );
	}

	/// <summary>
	/// Last source frame of the base animation. Leave unset to use the end of the take.
	/// </summary>
	[Property, Group( "Additive" ), WideMode, Title( "Trim End" ), ShowIf( nameof( AdditiveType ), AdditiveMode.RelativeToClip )]
	public int? AdditiveBaseEndFrame
	{
		get => ReadInt( AdditiveBaseEndFrameKey, -1 ) is var frame && frame >= 0 ? frame : null;
		set => _root[AdditiveBaseEndFrameKey] = Math.Max( value ?? -1, -1 );
	}

	/// <summary>
	/// Frame to subtract, relative to the trimmed base animation or this clip for Relative To Frame.
	/// </summary>
	[Property, Group( "Additive" ), WideMode, Title( "Reference Frame" ), ShowIf( nameof( HasBaseFrame ), true )]
	public int AdditiveBaseFrame
	{
		get => ReadInt( AdditiveBaseFrameKey, 0 );
		set => _root[AdditiveBaseFrameKey] = Math.Max( value, 0 );
	}

	private bool HasBaseFrame => AdditiveType is AdditiveMode.RelativeToClip or AdditiveMode.RelativeToFrame;
	private bool HasAdditive => AdditiveType != AdditiveMode.None;

	private string ReadString( string key ) => _root[key] is JsonValue value && value.TryGetValue( out string text ) ? text : string.Empty;
	private int ReadInt( string key, int fallback ) => _root[key] is JsonValue value && value.TryGetValue( out int number ) ? number : fallback;
	private float ReadFloat( string key, float fallback ) => _root[key] is JsonValue value && value.TryGetValue( out float number ) ? number : fallback;
	private bool ReadBool( string key, bool fallback ) => _root[key] is JsonValue value && value.TryGetValue( out bool flag ) ? flag : fallback;
	private void WriteVector( string key, Vector3 value ) => _root[key] = new JsonArray( value.x, value.y, value.z );
	private T ReadEnum<T>( string key, T fallback ) where T : struct, Enum => Enum.TryParse<T>( ReadString( key ), true, out var value ) ? value : fallback;

	private Vector3 ReadVector( string key )
	{
		if ( _root[key] is null )
			return Vector3.Zero;

		if ( _root[key] is not JsonArray { Count: 3 } values )
			throw new InvalidOperationException( $"Animation offset '{key}' must contain three numbers." );

		return new( values[0].GetValue<float>(), values[1].GetValue<float>(), values[2].GetValue<float>() );
	}

	private Axes ReadAxes( string prefix, Axes fallback )
		=> (ReadBool( prefix + "X", (fallback & Axes.X) != 0 ) ? Axes.X : Axes.None)
		| (ReadBool( prefix + "Y", (fallback & Axes.Y) != 0 ) ? Axes.Y : Axes.None)
		| (ReadBool( prefix + "Z", (fallback & Axes.Z) != 0 ) ? Axes.Z : Axes.None);

	private void WriteAxes( string prefix, Axes value )
	{
		_root[prefix + "X"] = (value & Axes.X) != 0;
		_root[prefix + "Y"] = (value & Axes.Y) != 0;
		_root[prefix + "Z"] = (value & Axes.Z) != 0;
	}

	private List<int> ReadMotionExtractionFrames()
		=> (_root[MotionExtractionFramesKey] as JsonArray ?? []).Select( frame => frame.GetValue<int>() ).ToList();

	private void UpdateMotionExtractionFrames()
	{
		if ( _motionExtractionFrames is null || _motionExtractionFrames.SequenceEqual( ReadMotionExtractionFrames() ) )
			return;

		_root[MotionExtractionFramesKey] = new JsonArray( _motionExtractionFrames.Select( frame => (JsonNode)JsonValue.Create( frame ) ).ToArray() );
	}

	/// <summary>
	/// Which foot is where, used to sync locomotion clips of different lengths to each other.
	/// </summary>
	public enum FootPhase
	{
		LeftFootDown = 0,
		RightFootPassing = 1,
		RightFootDown = 2,
		LeftFootPassing = 3,
		None = 4
	}

	/// <summary>
	/// One event on a track. Backed by the document it was read from, so members this doesn't
	/// know about are left exactly as they were found.
	/// </summary>
	public sealed class Event
	{
		internal Event( JsonObject json ) => Json = json;

		internal JsonObject Json { get; }

		/// <summary>
		/// Frame the event starts on. The compiler normalizes this against the clip's length, so
		/// an event stays where it was put if the clip's duration is overridden.
		/// </summary>
		[Property, Title( "Frame" )]
		public float StartFrame
		{
			get => Read( StartTimeKey );
			set => Json[StartTimeKey] = value;
		}

		/// <summary>
		/// How many frames the event lasts. Zero for an event that fires on a single frame.
		/// </summary>
		[Property, Title( "Length" )]
		public float DurationFrames
		{
			get => Read( DurationKey );
			set => Json[DurationKey] = MathF.Max( value, 0.0f );
		}

		public float EndFrame => StartFrame + DurationFrames;

		/// <summary>
		/// The tag an ID event carries, which is also what a sync track syncs on.
		/// </summary>
		[Property, Title( "ID" )]
		public string ID
		{
			get => ReadString( IDKey );
			set => Json[IDKey] = value ?? string.Empty;
		}

		/// <summary>
		/// The name a generic event carries, which is the game event it fires.
		/// </summary>
		[Property, Title( "Name" )]
		public string Name
		{
			get => ReadString( NameKey );
			set => Json[NameKey] = value ?? string.Empty;
		}

		/// <summary>
		/// Stored as its number rather than its name - that's what the runtime reads it as.
		/// </summary>
		[Property, Title( "Phase" )]
		public FootPhase Phase
		{
			get => (FootPhase)(int)Read( PhaseKey );
			set => Json[PhaseKey] = (int)value;
		}

		private float Read( string key )
		{
			if ( Json[key] is not JsonValue value )
				return 0f;

			if ( value.TryGetValue( out float number ) )
				return number;

			return value.TryGetValue( out int integer ) ? integer : 0f;
		}

		/// <summary>
		/// An ID is a string here, but a document written without the original string behind it
		/// carries the hash instead - which is nothing we can show, and nothing to fall over on.
		/// </summary>
		private string ReadString( string key ) => Json[key] is JsonValue value && value.TryGetValue( out string text ) ? text : null;
	}

	/// <summary>
	/// A row of events, all of one type. One track can be the clip's sync track, which divides it
	/// into the phases that clips of different lengths are blended by.
	/// </summary>
	public sealed class Track
	{
		internal Track( JsonObject json, List<Event> events )
		{
			Json = json;
			Events = events;
		}

		internal JsonObject Json { get; }

		public List<Event> Events { get; }

		public string EventClass => (Json[EventClassKey] is JsonValue value && value.TryGetValue( out string text ) ? text : null) ?? string.Empty;

		public bool IsSyncTrack
		{
			get => ReadFlag( SyncTrackKey );
			set => Json[SyncTrackKey] = value;
		}

		/// <summary>
		/// A disabled track keeps its events but compiles none of them.
		/// </summary>
		public bool IsDisabled
		{
			get => ReadFlag( DisabledKey );
			set => Json[DisabledKey] = value;
		}

		private bool ReadFlag( string key ) => Json[key] is JsonValue value && value.TryGetValue( out bool flag ) && flag;

		public Event AddEvent( float startFrame )
		{
			var json = new JsonObject
			{
				[StartTimeKey] = startFrame,
				[DurationKey] = 0.0f
			};

			var added = new Event( json );

			// Events on a track are the ones the compiler reads, so the array is the real list
			Array().Add( json );
			Events.Add( added );

			return added;
		}

		public void RemoveEvent( Event item )
		{
			var array = Array();
			var index = array.IndexOf( item.Json );

			// Gone from the document already means this list is the one out of step, and
			// dropping it here would only widen the gap
			if ( index < 0 )
				return;

			array.RemoveAt( index );
			Events.Remove( item );
		}

		private JsonArray Array()
		{
			if ( Json[EventsKey] is not JsonArray array )
				Json[EventsKey] = array = [];

			return array;
		}
	}

	/// <summary>
	/// Absolute path of the .anim this was read from, and what <see cref="Save"/> writes back to.
	/// </summary>
	public string FilePath { get; private set; }

	public List<Track> Tracks { get; } = [];

	private JsonObject _root;
	private JsonObject _savedState;
	private List<int> _motionExtractionFrames;
	private List<MorphWeight> _morphFrameWeights;

	public static ClipDocument Create( string skeletonName = "" )
	{
		var document = new ClipDocument
		{
			_root = new JsonObject { ["_class"] = "CClipDocument" }
		};
		document.SourceFilename = "";
		document.SkeletonName = skeletonName;
		document.Take = 0;
		document.StartFrame = null;
		document.EndFrame = null;
		document.ImportScale = 1f;
		document.AdditiveType = AdditiveMode.None;
		return document;
	}

	/// <summary>
	/// Read a .anim. Null if it isn't there or doesn't parse, having said which in the console.
	/// </summary>
	public static ClipDocument Load( string filePath )
	{
		if ( Kv3Document.Read( filePath ) is not { } root )
			return null;

		try
		{
			var document = new ClipDocument { FilePath = filePath, _root = root };
			document.RebuildTracks();
			document._savedState = document.CreateSnapshot();

			return document;
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Couldn't read {filePath}" );
			return null;
		}
	}

	/// <summary>
	/// Import settings and event tracks, as text an undo stack can hold onto.
	/// </summary>
	public string Snapshot()
	{
		_savedState ??= CreateSnapshot();
		return CreateSnapshot().ToJsonString();
	}

	/// <summary>
	/// Put a <see cref="Snapshot"/> back. The tracks and events are rebuilt from it, so anything
	/// holding one from before is stale and has to be looked up again.
	/// </summary>
	public void Restore( string snapshot )
	{
		try
		{
			var state = JsonNode.Parse( snapshot );
			if ( state is JsonArray tracks )
			{
				// Undo entries made before additive settings were editable contain only tracks.
				_root[TracksKey] = tracks;
			}
			else if ( state is JsonObject document && document[TracksKey] is JsonArray )
			{
				foreach ( var key in EditableKeys )
				{
					if ( document.ContainsKey( SourceFilenameKey ) || !ImportKeys.Contains( key ) )
						CopyMember( document, _root, key );
				}
			}
			else
			{
				throw new InvalidOperationException( "Invalid animation clip snapshot" );
			}
			_motionExtractionFrames = null;
			_morphFrameWeights = null;
			RebuildTracks();
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Couldn't restore {FilePath}" );
		}
	}

	private void RebuildTracks()
	{
		Tracks.Clear();

		foreach ( var track in _root[TracksKey] as JsonArray ?? [] )
		{
			if ( track is JsonObject json )
				Tracks.Add( new Track( json, ReadEvents( json ) ) );
		}
	}

	/// <summary>
	/// Write edited fields, preserving other source-file changes.
	/// </summary>
	public bool Save()
	{
		UpdateMorphFrameWeights();
		UpdateMotionExtractionFrames();
		if ( Kv3Document.Read( FilePath ) is not { } document )
		{
			Log.Warning( $"Couldn't write {FilePath} - it's gone or no longer reads as a clip" );
			return false;
		}

		foreach ( var key in EditableKeys )
		{
			if ( !JsonNode.DeepEquals( _root[key], _savedState?[key] ) )
				CopyMember( _root, document, key );
		}

		if ( !Kv3Document.Write( FilePath, document ) )
			return false;

		_savedState = CreateSnapshot();
		return true;
	}

	public bool SaveAs( string filePath )
	{
		UpdateMorphFrameWeights();
		UpdateMotionExtractionFrames();
		filePath = System.IO.Path.GetFullPath( filePath );
		System.IO.Directory.CreateDirectory( System.IO.Path.GetDirectoryName( filePath ) );
		if ( !Kv3Document.Write( filePath, _root, overwrite: false ) )
			return false;

		FilePath = filePath;
		_savedState = CreateSnapshot();
		return true;
	}

	private JsonObject CreateSnapshot()
	{
		UpdateMorphFrameWeights();
		UpdateMotionExtractionFrames();
		TrackArray();
		var snapshot = new JsonObject();
		foreach ( var key in EditableKeys )
			CopyMember( _root, snapshot, key );
		return snapshot;
	}

	private static void CopyMember( JsonObject source, JsonObject target, string key )
	{
		if ( source.TryGetPropertyValue( key, out var value ) )
			target[key] = value?.DeepClone();
		else
			target.Remove( key );
	}

	public Track AddTrack( string eventClass )
	{
		var json = new JsonObject
		{
			[EventClassKey] = eventClass,
			[SyncTrackKey] = false,
			[DisabledKey] = false,
			[EventsKey] = new JsonArray()
		};

		var track = new Track( json, [] );

		TrackArray().Add( json );
		Tracks.Add( track );

		return track;
	}

	public void RemoveTrack( Track track )
	{
		var array = TrackArray();
		var index = array.IndexOf( track.Json );

		if ( index < 0 )
			return;

		array.RemoveAt( index );
		Tracks.Remove( track );
	}

	/// <summary>
	/// Only one track can be the sync track - the compiler takes the first and warns about the rest.
	/// </summary>
	public void SetSyncTrack( Track track, bool sync )
	{
		foreach ( var other in Tracks )
		{
			other.IsSyncTrack = sync && other == track;
		}
	}

	private JsonArray TrackArray()
	{
		if ( _root[TracksKey] is not JsonArray array )
			_root[TracksKey] = array = [];

		return array;
	}

	private const string TracksKey = "m_eventTracks";
	private const string AdditiveTypeKey = "m_additiveType";
	private const string AdditiveModelSpaceKey = "m_bAdditiveModelSpace";
	private const string AdditiveBaseFileKey = "m_additiveBaseFilename";
	private const string AdditiveBaseTakeKey = "m_nAdditiveBaseTake";
	private const string AdditiveBaseStartFrameKey = "m_nAdditiveBaseStartFrame";
	private const string AdditiveBaseEndFrameKey = "m_nAdditiveBaseEndFrame";
	private const string AdditiveBaseFrameKey = "m_nAdditiveBaseFrameIdx";
	[SkipHotload]
	private static readonly string[] ImportKeys =
	[
		SourceFilenameKey, SkeletonKey, TakeKey, StartFrameKey, EndFrameKey, ImportScaleKey, ImportFrameRateKey, DurationOverrideKey,
		SmoothLoopKey, SmoothLoopFramesFromStartKey, SmoothLoopFramesFromEndKey,
		StartLoopFrameKey, LinearDeltaKey, LinearDeltaSplineKey, MorphFrameWeightsKey,
		AlignmentReferenceKey, AlignmentBoneKey, AlignmentFrameModeKey, AlignmentFrameKey,
		AlignmentReferenceFrameModeKey, AlignmentReferenceFrameKey,
		"m_bAlignTX", "m_bAlignTY", "m_bAlignTZ", "m_bAlignRX", "m_bAlignRY", "m_bAlignRZ",
		ExtractMotionKey, MotionExtractionBoneKey, "m_bExtractMotionTX", "m_bExtractMotionTY", "m_bExtractMotionTZ",
		MotionExtractionYawKey, MotionExtractionLinearKey, MotionExtractionQuadraticKey, MotionExtractionModeKey, MotionExtractionFramesKey,
		PreExtractionTranslationKey, PreExtractionRotationKey, PostExtractionTranslationKey, PostExtractionRotationKey
	];
	[SkipHotload]
	private static readonly string[] EditableKeys =
	[
		.. ImportKeys,
		TracksKey, AdditiveTypeKey, AdditiveModelSpaceKey, AdditiveBaseFileKey, AdditiveBaseTakeKey,
		AdditiveBaseStartFrameKey, AdditiveBaseEndFrameKey, AdditiveBaseFrameKey
	];
	private const string SkeletonKey = "m_animationSkeletonName";
	private const string SourceFilenameKey = "m_sourceFilename";
	private const string TakeKey = "m_nTake";
	private const string StartFrameKey = "m_nStartFrame";
	private const string EndFrameKey = "m_nEndFrame";
	private const string ImportScaleKey = "m_flImportScale";
	private const string ImportFrameRateKey = "m_flImportFrameRate";
	private const string DurationOverrideKey = "m_flDurationOverrideSeconds";
	private const string SmoothLoopKey = "m_bSmoothLoop";
	private const string SmoothLoopFramesFromStartKey = "m_nSmoothLoopFramesFromStart";
	private const string SmoothLoopFramesFromEndKey = "m_nSmoothLoopFramesFromEnd";
	private const string StartLoopFrameKey = "m_nStartLoopFrame";
	private const string LinearDeltaKey = "m_bLinearDelta";
	private const string LinearDeltaSplineKey = "m_bLinearDeltaSpline";
	private const string MorphFrameWeightsKey = "m_morphFrameWeights";
	private const string AlignmentReferenceKey = "m_alignToClipFilename";
	private const string AlignmentBoneKey = "m_alignBone";
	private const string AlignmentFrameModeKey = "m_alignFrameType";
	private const string AlignmentFrameKey = "m_nAlignFrameIdx";
	private const string AlignmentReferenceFrameModeKey = "m_alignToFrameType";
	private const string AlignmentReferenceFrameKey = "m_nAlignToFrameIdx";
	private const string ExtractMotionKey = "m_bExtractMotion";
	private const string MotionExtractionBoneKey = "m_motionExtractionBone";
	private const string MotionExtractionYawKey = "m_bExtractMotionRZ";
	private const string MotionExtractionLinearKey = "m_bExtractMotionLinear";
	private const string MotionExtractionQuadraticKey = "m_bExtractMotionQuadratic";
	private const string MotionExtractionModeKey = "m_motionExtractionType";
	private const string MotionExtractionFramesKey = "m_motionExtractionFrames";
	private const string PreExtractionTranslationKey = "m_preExtractionTranslation";
	private const string PreExtractionRotationKey = "m_preExtractionRotation";
	private const string PostExtractionTranslationKey = "m_postExtractionTranslation";
	private const string PostExtractionRotationKey = "m_postExtractionRotation";
	private const string EventClassKey = "m_eventClassName";
	private const string SyncTrackKey = "m_bIsSyncTrack";
	private const string DisabledKey = "m_bIsDisabled";
	private const string EventsKey = "m_events";
	private const string StartTimeKey = "m_startTime";
	private const string DurationKey = "m_duration";
	private const string IDKey = "m_ID";
	private const string NameKey = "m_name";
	private const string PhaseKey = "m_phase";

	private static List<Event> ReadEvents( JsonObject track )
	{
		var events = new List<Event>();

		foreach ( var item in track[EventsKey] as JsonArray ?? [] )
		{
			if ( item is JsonObject json )
				events.Add( new Event( json ) );
		}

		return events;
	}
}
