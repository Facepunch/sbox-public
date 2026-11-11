using Basalt;
using Basalt.Backend;
using Basalt.Gpu;
using Basalt.Gpu.Source2;
using Basalt.Source2;
using Basalt.Source2.Models;
using GpuDevice = Basalt.Gpu.Device;

namespace Sandbox.BasaltLab;

/// <summary>
/// <c>-model-parity folder [-model path] [-mesh-groups mask] [-tolerance N] [-validation]</c>: renders s&amp;box models
/// with Basalt twice in one process - on Source 2's device, with the engine's own model loading, and on a Basalt.Gpu
/// device of its own, with the model read by <see cref="ModelFile"/> - from the same camera under the same sun, and
/// compares the pixels. Each case writes <c>native.png</c>, <c>basalt.png</c> and <c>diff.png</c> (differences x8) to a
/// folder of its own, and the run exits 1 when any case's mean channel difference is over <c>-tolerance</c> (default 1,
/// in 8-bit steps) or validation reported errors.
/// <para>
/// It also checks the reader against the engine: vertex, triangle and draw counts, LOD count and switch distances,
/// bounds and draw materials, as <c>Model.MeshInfo</c> reports them, and bones and their bind pose. By default it runs a
/// set of cases covering a dev box, a prop with several materials and LODs (near and far), body groups, the skinned
/// citizen in its bind pose, and the human posed by native's animation system from a few sequences (the same bones given
/// to both devices), one of them dressed.
/// </para>
/// </summary>
internal static class ModelParity
{
	const int Width = 400, Height = 400;

	/// <summary>
	/// A case: a model, optionally posed from one of its sequences at a normalized time, dressed in clothing models whose
	/// bones follow the body's by name, and with some of its morphs (flex controllers) set. The camera looks at the height
	/// <paramref name="Aim"/> of the way up its bounds.
	/// </summary>
	sealed record Case( string Name, string Path, ulong? MeshGroups = null, float Distance = 1.0f, string Sequence = null, float Time = 0, string[] Clothing = null, int Morphs = 0, float Aim = 0.5f );

	const string Human = "models/citizen_human/human.vmdl";

	static readonly string[] Outfit =
	[
		"models/citizen_clothes/trousers/Jeans/Models/jeans_m_human.vmdl",
		"models/citizen_clothes/jacket/Hoodie/Models/hoodie_m_human.vmdl",
		"models/citizen_clothes/shoes/Boots/Models/boots_m_human.vmdl",
		"models/citizen_clothes/hair/braided_long_beard/Models/braided_long_beard_m_human.vmdl",
	];

	static readonly Case[] Cases =
	[
		new( "box", "models/dev/box.vmdl" ),
		new( "gordon", "models/dev/gordon_at_desk.vmdl" ),
		new( "trashcan", "models/citizen_props/trashcan01.vmdl" ),
		new( "trashcan-near", "models/citizen_props/trashcan01.vmdl", Distance: 0.4f ),
		new( "trashcan-far", "models/citizen_props/trashcan01.vmdl", Distance: 10 ),
		new( "citizen", "models/citizen/citizen.vmdl" ),
		// Body groups: Head and Feet on their empty second choices, the rest on their first
		new( "citizen-bodygroups", "models/citizen/citizen.vmdl", MeshGroups: 2 | 4 | 16 | 64 | 512 ),
		new( "human", Human ),
		// Posed by native's animation system, the same bones given to both devices
		new( "human-wave", Human, Sequence: "AvatarMenu_Entry_Wave_01", Time: 0.5f ),
		new( "human-jump", Human, Sequence: "AvatarMenu_Entry_Jump", Time: 0.4f ),
		new( "human-sit", Human, Sequence: "SitPose_Default" ),
		new( "human-dressed", Human, Sequence: "AvatarMenu_FidgetLarge_03", Time: 0.5f, Clothing: Outfit ),
		// Morphs: a few of the face's flex controllers set, both devices compositing and skinning them
		new( "human-face", Human, Distance: 0.22f, Morphs: 6, Aim: 0.93f ),
		new( "human-face-posed", Human, Distance: 0.5f, Sequence: "AvatarMenu_Entry_Wave_01", Time: 0.5f, Morphs: 12, Aim: 0.8f ),
	];

	/// <summary>The controllers a morph case sets: every 7th of the model's, at weights between 0.5 and 1.</summary>
	static (string Name, float Weight)[] MorphWeights( Model model, int count )
	{
		var names = model.Morphs?.Names ?? [];
		return names.Length == 0 ? [] : Enumerable.Range( 0, count ).Select( i => (names[i * 7 % names.Length], 0.5f + 0.5f * ((i * 3) % 5) / 4.0f) ).DistinctBy( x => x.Item1 ).ToArray();
	}

	/// <summary>
	/// A native model's morphs as Basalt on Source 2's device queues them (<c>SceneModelMorphs</c>): native evaluates the
	/// scene model's flex controllers for each mesh.
	/// </summary>
	sealed class NativeMorphs( SceneModel source ) : IMorphSource
	{
		public int Queue( GpuMesh mesh, int modelMesh ) => source.IsValid() ? NativeEngine.RenderTools.QueueModelMeshMorph( source, mesh.Native(), modelMesh ) : -1;
	}

	/// <summary>A posed body's world bones and bounds, and each clothing model's bones following it.</summary>
	sealed record Pose( Transform[] Body, Transform[][] Clothing, BBox Bounds );

	public static int Run( Source2Backend backend, string folder )
	{
		var cases = Args.String( "-model" ) is { } single
			? [new Case( System.IO.Path.GetFileNameWithoutExtension( single ), single, ulong.TryParse( Args.String( "-mesh-groups" ), out var mask ) ? mask : null )]
			: Cases;

		var tolerance = double.TryParse( Args.String( "-tolerance" ), System.Globalization.CultureInfo.InvariantCulture, out var t ) ? t : 1.0;
		folder = System.IO.Path.GetFullPath( folder );

		using var gpu = GpuDevice.Create( new DeviceOptions { ApplicationName = "Basalt Lab model parity", Validation = Args.Has( "-validation" ), Presentation = false, Message = m => Log.Info( $"Basalt.Gpu: {m.Severity} {m.Text}" ) } );
		using var device = new BackendDevice( gpu );
		backend.Phase( $"Basalt.Gpu device ({gpu.Adapter.Name})" );

		var failed = 0;
		var lines = new List<string>();
		foreach ( var test in cases )
		{
			var (line, ok) = RunCase( backend, device, test, System.IO.Path.Combine( folder, test.Name ), tolerance );
			lines.Add( line );
			Log.Info( line );
			Console.WriteLine( line );
			if ( !ok ) failed++;
		}

		gpu.WaitIdle();

		var summary = $"Model parity: {cases.Length - failed} of {cases.Length} cases within {tolerance:F2} mean difference ({folder}); Basalt.Gpu validation errors {gpu.ValidationErrors}";
		Log.Info( summary );
		Console.WriteLine( summary );
		return failed == 0 && gpu.ValidationErrors == 0 ? 0 : 1;
	}

	static (string Line, bool Ok) RunCase( Source2Backend backend, BackendDevice device, Case test, string folder, double tolerance )
	{
		var model = Model.Load( test.Path );
		if ( model is null || model.IsError ) return ($"{test.Name}: {test.Path} didn't load on Source 2", false);

		var file = ModelFile.Load( device.Content, test.Path );
		comparedDraws = 0;
		var mismatches = Compare( model, file );

		// Clothing: the engine's models and the reader's, checked the same way
		var clothing = (test.Clothing ?? []).Select( path => (Path: path, Model: Model.Load( path ), File: ModelFile.Load( device.Content, path )) ).ToArray();
		foreach ( var (path, clothes, clothesFile) in clothing )
		{
			if ( clothes is null || clothes.IsError ) return ($"{test.Name}: {path} didn't load on Source 2", false);
			mismatches.AddRange( Compare( clothes, clothesFile ).Select( x => $"{System.IO.Path.GetFileNameWithoutExtension( path )}: {x}" ) );
		}

		// Posed by native's animation system: both devices get the same bones
		var pose = test.Sequence is null ? null : Posed( model, test.Sequence, test.Time, clothing.Select( x => x.Model ).ToArray() );

		// Morphs: a native scene model with the controllers set, never drawn, which native evaluates for Source 2's device;
		// the same controllers by name for Basalt.Gpu's
		var weights = MorphWeights( model, test.Morphs );
		SceneWorld morphWorld = null;
		IMorphSource nativeMorphs = null;
		if ( weights.Length > 0 )
		{
			morphWorld = new SceneWorld();
			var face = new SceneModel( morphWorld, model, Transform.Zero ) { UseAnimGraph = false, PlaybackRate = 0 };
			// At once: the collection's own Set fades them in, and the scene model is never updated again
			foreach ( var (name, weight) in weights ) face.Morphs.Set( name, weight, 0 );
			face.Update( 0 );
			nativeMorphs = new NativeMorphs( face );
		}

		var gpuMorphs = new BackendMorphs( device );
		foreach ( var (name, weight) in weights ) gpuMorphs.Set( name, weight );

		// The same world and camera on both devices: the model at the origin, a sun from the side, the default ambient
		Shot MakeWorld( RenderMesh mesh, RenderMesh[] clothes, IMorphSource morphs )
		{
			var world = new RenderWorld();
			world.Lighting.SunDirection = new Vector3( -0.4f, 0.6f, -0.7f ).Normal;
			world.Lighting.SunColor = new Color( 1.2f, 1.1f, 1.0f );

			var body = new MeshObject( mesh, Transform.Zero ) { MorphSource = weights.Length > 0 ? morphs : null };
			world.Add( body );
			if ( pose is not null )
			{
				body.SetBones( pose.Body );
				body.LocalBounds = pose.Bounds;
				for ( int i = 0; i < clothes.Length; i++ )
				{
					var worn = new MeshObject( clothes[i], Transform.Zero );
					worn.SetBones( pose.Clothing[i] );
					worn.LocalBounds = pose.Bounds;
					world.Add( worn );
				}
			}

			var bounds = pose?.Bounds ?? mesh.Bounds;
			var size = bounds.Size.Length;
			var view = new RenderView { Name = "Model parity", FieldOfView = 60, ClearColor = new Color( 0.1f, 0.12f, 0.16f ), Viewport = new Rect( 0, 0, Width, Height ) };
			view.Rotation = Rotation.From( 20, 160, 0 );
			var aim = bounds.Center.WithZ( bounds.Mins.z + bounds.Size.z * test.Aim );
			view.Position = aim - view.Rotation.Forward * size * 1.1f * test.Distance;
			view.ZNear = size * 0.05f;
			view.ZFar = size * 10 * test.Distance;
			return new Shot( world, view );
		}

		// Source 2: the engine's model, as BasaltLab's scenes draw it
		// (Source2Mesh draws every mesh; body groups are applied per object, here the case's or the model's default ones)
		var nativeMesh = WithMeshGroups( Source2Mesh.FromModel( model ), model, test.MeshGroups ?? file.DefaultMeshGroupMask );
		var nativeClothes = clothing.Select( x => WithMeshGroups( Source2Mesh.FromModel( x.Model ), x.Model, x.File.DefaultMeshGroupMask ) ).ToArray();
		var native = Render( MakeWorld( nativeMesh, nativeClothes, nativeMorphs ), backend.ReadPixels, backend.BeginFrame, backend.EndFrame );
		backend.Phase( $"{test.Name}: Source 2" );
		if ( morphWorld is not null )
		{
			morphWorld.Delete();
			SceneWorld.FlushQueuedDeletes();
		}

		// Basalt.Gpu: the model read by Basalt.Source2
		var previous = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( device );
		Color32[] basalt;
		try
		{
			var mesh = BackendModel.Create( device, file, test.MeshGroups );
			var clothes = clothing.Select( x => BackendModel.Create( device, x.File ) ).ToArray();
			basalt = Render( MakeWorld( mesh, clothes, gpuMorphs ), target => ReadBack( device, target ), device.BeginFrame );
		}
		finally
		{
			RenderHost.Current = previous;
		}

		backend.Phase( $"{test.Name}: Basalt.Gpu" );

		// Compare
		var diff = new Color32[native.Length];
		long total = 0;
		int maxDiff = 0, over = 0;
		for ( int i = 0; i < native.Length; i++ )
		{
			var a = native[i];
			var b = basalt[i];
			var dr = Math.Abs( a.r - b.r );
			var dg = Math.Abs( a.g - b.g );
			var db = Math.Abs( a.b - b.b );
			total += dr + dg + db;
			var d = Math.Max( dr, Math.Max( dg, db ) );
			maxDiff = Math.Max( maxDiff, d );
			if ( d > 8 ) over++;
			var v = (byte)Math.Min( 255, d * 8 );
			diff[i] = new Color32( v, v, v, 255 );
		}

		var mean = total / (3.0 * native.Length);
		System.IO.Directory.CreateDirectory( folder );
		Source2Backend.SavePng( native, Width, Height, System.IO.Path.Combine( folder, "native.png" ) );
		Source2Backend.SavePng( basalt, Width, Height, System.IO.Path.Combine( folder, "basalt.png" ) );
		Source2Backend.SavePng( diff, Width, Height, System.IO.Path.Combine( folder, "diff.png" ) );

		var info = model.MeshInfo;
		var ok = mean <= tolerance && mismatches.Count == 0;
		var posed = (test.Sequence is null ? "" : $" posed {test.Sequence} at {test.Time:F2}{(clothing.Length > 0 ? $", {clothing.Length} clothing models" : "")}")
			+ (weights.Length > 0 ? $" morphs {string.Join( ", ", weights.Select( x => $"{x.Name} {x.Weight:F2}" ) )}" : "");
		var line = $"{test.Name}: {test.Path}{(test.MeshGroups is { } m ? $" mesh groups {m}" : "")}{posed}: mean difference {mean:F3}, max {maxDiff}, {over} pixels over 8; "
			+ $"engine {info.TotalVertices} vertices, {info.TotalTriangles} triangles, {info.TotalDrawCalls} draws, {info.LodCount} LODs, bounds {model.Bounds.Mins} {model.Bounds.Maxs}"
			+ $"; {comparedDraws} draws' vertices compared"
			+ (mismatches.Count == 0 ? "; reader matches" : $"; reader differs: {string.Join( "; ", mismatches )}")
			+ (ok ? "" : " FAIL");
		return (line, ok);
	}

	sealed record Shot( RenderWorld Scene, RenderView View );

	/// <summary>
	/// The body posed by a native <see cref="SceneModel"/> from <paramref name="sequence"/>, frozen at
	/// <paramref name="time"/> (0 to 1), as BasaltLab's skinned presets pose it; and each clothing model's bones following
	/// the body's by name, as bone merging does, with bones the body doesn't have kept where their bind pose puts them
	/// under their parent.
	/// </summary>
	static Pose Posed( Model model, string sequence, float time, Model[] clothing )
	{
		var world = new SceneWorld();
		try
		{
			var animator = new SceneModel( world, model, Transform.Zero ) { UseAnimGraph = false, PlaybackRate = 0 };
			animator.CurrentSequence.Name = sequence;
			animator.CurrentSequence.TimeNormalized = time;
			animator.Update( 0 );

			var body = new Transform[model.BoneCount];
			animator.GetBoneWorldTransforms( body );
			var bounds = animator.Bounds;
			animator.Delete();

			var byName = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase );
			for ( int b = 0; b < body.Length; b++ ) byName.TryAdd( model.GetBoneName( b ), b );

			var worn = new Transform[clothing.Length][];
			for ( int c = 0; c < clothing.Length; c++ )
			{
				var clothes = clothing[c];
				var bones = worn[c] = new Transform[clothes.BoneCount];
				for ( int b = 0; b < bones.Length; b++ )
				{
					if ( byName.TryGetValue( clothes.GetBoneName( b ), out var match ) )
					{
						bones[b] = body[match];
						continue;
					}

					var parent = clothes.GetBoneParent( b );
					var bind = clothes.GetBoneTransform( b );
					bones[b] = parent >= 0 && parent < b ? bones[parent].ToWorld( clothes.GetBoneTransform( parent ).ToLocal( bind ) ) : bind;
				}
			}

			return new Pose( body, worn, bounds );
		}
		finally
		{
			// Deleted now rather than at a frame's end, so nothing it posed with is still held at shutdown
			world.Delete();
			SceneWorld.FlushQueuedDeletes();
		}
	}

	/// <summary>Frames rendered before the one compared (<c>-frames</c>, default 120: native streams textures in over the first frames).</summary>
	static int Frames => Math.Max( Args.Int( "-frames", 120 ), 1 );

	/// <summary>Render a few frames (shadows settle) into a target of the current host's device and read the last.</summary>
	static Color32[] Render( Shot world, Func<ViewTarget, Color32[]> readback, Action beginFrame, Action endFrame = null )
	{
		using var system = new RenderSystem { ParallelRecording = false };
		using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Model parity" );
		for ( int frame = 0; frame < Frames; frame++ )
		{
			beginFrame?.Invoke();
			RenderHost.Current.NextFrame();
			system.Render( world.Scene, world.View, target );
			endFrame?.Invoke();
		}

		return readback( target );
	}

	/// <summary>Hide the draws of meshes outside <paramref name="mask"/>, as body groups do.</summary>
	static RenderMesh WithMeshGroups( RenderMesh mesh, Model model, ulong mask )
	{
		var states = new List<RenderMesh.DrawState>();
		var meshes = model.MeshInfo.Meshes;
		for ( int lod = 0; lod < mesh.LodCount; lod++ )
		{
			foreach ( var draw in mesh.DrawsForLod( lod ) )
			{
				if ( (meshes[draw.Mesh].MeshGroupMask & mask) != 0 ) continue;
				states.Add( new RenderMesh.DrawState( draw.Mesh, draw.DrawCall, draw.Material, Visible: false, draw.Translucent, draw.ShadowFastPath ) );
			}
		}

		return mesh.WithDraws( states.ToArray() );
	}

	/// <summary>What the reader says against what the engine reports.</summary>
	static List<string> Compare( Model model, ModelFile file )
	{
		var errors = new List<string>();
		var info = model.MeshInfo;
		void Check<T>( string what, T engine, T reader )
		{
			if ( !EqualityComparer<T>.Default.Equals( engine, reader ) ) errors.Add( $"{what} {engine} != {reader}" );
		}

		Check( "meshes", info.Meshes.Length, file.Meshes.Count );
		Check( "vertices", info.TotalVertices, file.Meshes.Sum( m => m.VertexCount ) );
		Check( "triangles", info.TotalTriangles, file.Meshes.Sum( m => m.TriangleCount ) );
		Check( "draws", info.TotalDrawCalls, file.Meshes.Sum( m => m.DrawCalls.Count ) );
		Check( "LODs", Math.Max( 1, info.LodCount ), file.LodCount );
		Check( "switch distances", string.Join( ",", info.LodSwitchDistances ?? [] ), string.Join( ",", file.LodSwitchDistances ) );
		Check( "bounds", (model.Bounds.Mins, model.Bounds.Maxs), (new Vector3( file.Mins.X, file.Mins.Y, file.Mins.Z ), new Vector3( file.Maxs.X, file.Maxs.Y, file.Maxs.Z )) );

		// Bones in the engine's order with its bind pose, since poses are given by model bone index
		Check( "bones", model.BoneCount, file.Skeleton.Count );
		var bindPose = file.Skeleton.BindPose();
		var bindPoseErrors = 0;
		for ( int b = 0; b < Math.Min( model.BoneCount, bindPose.Length ); b++ )
		{
			var engine = model.GetBoneTransform( b );
			var reader = bindPose[b];
			if ( !string.Equals( model.GetBoneName( b ), file.Skeleton.Names[b], StringComparison.OrdinalIgnoreCase ) || (engine.Position - new Vector3( reader.M41, reader.M42, reader.M43 )).Length > 1e-2f ) bindPoseErrors++;
		}

		if ( bindPoseErrors > 0 ) errors.Add( $"{bindPoseErrors} bones' names or bind positions differ" );

		// Skinned meshes: weights per vertex and bones, as native's model gives them to Basalt
		for ( int m = 0; m < Math.Min( info.Meshes.Length, file.Meshes.Count ); m++ )
		{
			if ( !file.Meshes[m].IsSkinned ) continue;
			var masters = file.MeshBoneToModelBone( m );
			for ( int i = 0; i < masters.Length; i++ )
			{
				if ( (uint)masters[i] >= (uint)file.Skeleton.Count || !string.Equals( file.Meshes[m].Bones[i].Name, file.Skeleton.Names[masters[i]], StringComparison.OrdinalIgnoreCase ) )
				{
					errors.Add( $"mesh {m} bone {i} ({file.Meshes[m].Bones[i].Name}) maps to model bone {masters[i]}" );
					break;
				}
			}
		}

		for ( int m = 0; m < Math.Min( info.Meshes.Length, file.Meshes.Count ); m++ )
		{
			var a = info.Meshes[m];
			var b = file.Meshes[m];
			Check( $"mesh {m} LOD mask", a.LodMask, (int)b.LodMask );
			Check( $"mesh {m} mesh groups", a.MeshGroupMask, b.MeshGroupMask );
			Check( $"mesh {m} draws", a.DrawCalls.Length, b.DrawCalls.Count );
			for ( int d = 0; d < Math.Min( a.DrawCalls.Length, b.DrawCalls.Count ); d++ )
			{
				var x = a.DrawCalls[d];
				var y = b.DrawCalls[d];
				Check( $"mesh {m} draw {d} range", (x.StartIndex, x.Indices, x.Vertices, x.BaseVertex), (y.StartIndex, y.IndexCount, y.VertexCount, y.BaseVertex) );
				Check( $"mesh {m} draw {d} material", x.Material?.ResourcePath?.ToLowerInvariant(), y.Material?.ToLowerInvariant() );
				CompareVertices( model, file, m, d, errors );
			}
		}

		return errors;
	}

	/// <summary>
	/// A draw's vertices as the engine reads them back from its own GPU buffers (<c>Model.TryReadDrawCall</c>) against
	/// the reader's decoded ones, index by index: position, uv, normal and tangent.
	/// </summary>
	static int comparedDraws;

	static void CompareVertices( Model model, ModelFile file, int mesh, int draw, List<string> errors )
	{
		if ( !model.TryReadDrawCall( mesh, draw, out var engine, out var engineIndices, out _ ) ) return;
		comparedDraws++;

		var call = file.Meshes[mesh].DrawCalls[draw];
		var buffer = file.Meshes[mesh].VertexBuffers[call.VertexBuffers[0].Buffer];
		var indices = file.Meshes[mesh].IndexBuffers[call.IndexBuffer.Buffer];
		var position = buffer.Find( "POSITION" ).Value;
		var uv = buffer.Find( "TEXCOORD" );
		var normal = buffer.Find( "NORMAL" );
		var tangent = buffer.Find( "TANGENT" );
		int positions = 0, uvs = 0, normals = 0, tangents = 0;

		for ( int i = 0; i < engineIndices.Length; i++ )
		{
			var a = engine[engineIndices[i]];
			var v = (int)indices[call.StartIndex + i] + call.BaseVertex;
			var p = buffer.Read( position, v );
			if ( (new Vector3( p.X, p.Y, p.Z ) - a.Position).Length > 1e-3f ) positions++;
			if ( uv is { } u )
			{
				var t = buffer.Read( u, v );
				if ( MathF.Abs( t.X - a.TexCoord0.x ) > 1e-3f || MathF.Abs( t.Y - a.TexCoord0.y ) > 1e-3f ) uvs++;
			}

			if ( normal is not { } n ) continue;
			System.Numerics.Vector3 nn;
			System.Numerics.Vector4 tt;
			if ( n.Format is ColorFormat.R10G10B10A2_UNORM or ColorFormat.R8G8B8A8_UNORM )
			{
				var packed = BitConverter.ToUInt32( buffer.Data, v * buffer.Stride + n.Offset );
				(nn, tt) = n.Format == ColorFormat.R10G10B10A2_UNORM ? TangentFrame.UnpackOctDiamond( packed ) : TangentFrame.UnpackLegacy( packed );
			}
			else
			{
				var r = buffer.Read( n, v );
				nn = new System.Numerics.Vector3( r.X, r.Y, r.Z );
				tt = tangent is { } tf ? buffer.Read( tf, v ) : default;
			}

			if ( Vector3.Dot( new Vector3( nn.X, nn.Y, nn.Z ), a.Normal ) < 0.999f ) normals++;
			if ( Vector3.Dot( new Vector3( tt.X, tt.Y, tt.Z ), new Vector3( a.Tangent.x, a.Tangent.y, a.Tangent.z ) ) < 0.99f || MathF.Sign( tt.W ) != MathF.Sign( a.Tangent.w ) ) tangents++;
		}

		if ( positions + uvs + normals + tangents > 0 )
			errors.Add( $"mesh {mesh} draw {draw} of {engineIndices.Length} indices: {positions} positions, {uvs} uvs, {normals} normals, {tangents} tangents differ (first engine tangent {engine[engineIndices[0]].Tangent})" );
	}

	/// <summary>A Basalt.Gpu target's colour as 8-bit sRGB, encoded as <see cref="Source2Backend.ReadPixels"/> encodes.</summary>
	static Color32[] ReadBack( BackendDevice device, ViewTarget target )
	{
		var gpu = device.Gpu;
		var (texture, layout) = BackendDevice.Unwrap( target.Color );
		var bytes = Width * Height * 8;
		var buffer = gpu.CreateBuffer( new BufferInfo { Size = bytes, Memory = MemoryLocation.Readback, Name = "Model parity readback" } );
		var list = gpu.BeginCommandList( QueueType.Graphics, "Model parity readback" );
		list.Barrier( Basalt.Gpu.TextureBarrier.Transition( texture, layout, ImageLayout.CopySource, PipelineStage.AllCommands, MemoryAccess.MemoryWrite, PipelineStage.Transfer, MemoryAccess.TransferRead ) );
		list.CopyTextureToBuffer( texture, 0, 0, buffer );
		list.Barrier( new BufferBarrier { Buffer = buffer, SourceStage = PipelineStage.Transfer, SourceAccess = MemoryAccess.TransferWrite, DestinationStage = PipelineStage.Host, DestinationAccess = MemoryAccess.HostRead } );
		gpu.Wait( gpu.Submit( list ) );
		BackendDevice.SetLayout( target.Color, ImageLayout.CopySource );

		var halves = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, ushort>( gpu.Map( buffer ) );
		var pixels = new Color32[Width * Height];
		for ( int i = 0; i < pixels.Length; i++ )
			pixels[i] = new Color32( Encode( halves[i * 4] ), Encode( halves[i * 4 + 1] ), Encode( halves[i * 4 + 2] ), 255 );

		gpu.Destroy( buffer );
		return pixels;
	}

	static byte Encode( ushort half )
	{
		var linear = Math.Clamp( (float)BitConverter.UInt16BitsToHalf( half ), 0, 1 );
		var gamma = linear <= 0.0031308f ? linear * 12.92f : 1.055f * MathF.Pow( linear, 1.0f / 2.4f ) - 0.055f;
		return (byte)MathF.Round( gamma * 255 );
	}
}
