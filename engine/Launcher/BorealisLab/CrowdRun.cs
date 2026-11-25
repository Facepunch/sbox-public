using Borealis;
using Borealis.Backend;
using Borealis.Gpu;
using Borealis.Sdl;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using Capture = BorealisGpuSample.Capture;
using GpuDevice = Borealis.Gpu.Device;
using GpuTextureBarrier = Borealis.Gpu.TextureBarrier;

namespace Sandbox.BorealisLab;

/// <summary>
/// <c>-crowd</c>: SceneLab's Crowd Near on a Borealis.Gpu device of its own, in a window: 240 dressed humans in a plaza from
/// the same seed, a third walking slow circles, every one animated each frame by a native <see cref="SceneModel"/> playing a
/// sequence (never drawn: native's animation system only poses it) with four face morphs moving, and the camera drifting.
/// Borealis draws it with the models, materials and textures Borealis.Source2 reads, skinning and morphs on Borealis.Gpu. Timed
/// per run of frames as <c>-construct -real</c> times the map: Borealis's render and the copy into the window on the main
/// thread (animation isn't counted), and GPU time between timestamps.
/// <list type="bullet">
/// <item><c>-count N</c> (240) citizens; <c>-frames N</c> (600) measured and <c>-warmup N</c> (120 frames and 2 s) frames per run.</item>
/// <item><c>-width</c> / <c>-height</c> (1280x720), <c>-msaa N</c> (4), <c>-vsync</c> (off by default).</item>
/// <item><c>-variants serial,parallel</c> - recording on one thread or on workers; both by default.</item>
/// <item><c>-capture path.png</c> - save the first measured frame; <c>-out folder</c> - where <c>crowd.md</c> and <c>crowd.csv</c> go.</item>
/// <item><c>-validation</c> - the Khronos validation layer.</item>
/// <item><c>-crowd-eyes path.vmat</c> - the citizens' eyes in another material (eyeball.shader's, to time that shader on faces).</item>
/// </list>
/// What differs from SceneLab's: sequences (looped every four seconds) rather than the citizens' animation graph, a fixed list of clothing models
/// rather than the clothing resources (no body groups hidden under them), and no 2D sky or post processing.
/// </summary>
internal static class CrowdRun
{
	public static bool Requested => Args.Has( "-crowd" );

	const int LoopFrames = 600;
	const float Extent = 700;
	const string Human = "models/citizen_human/human.vmdl";
	const string Clothes = "models/citizen_clothes";

	static readonly string[] Hair =
	[
		$"{Clothes}/hair/hair_bobcut/Models/hair_bobcut_m_human.vmdl", $"{Clothes}/hair/hair_longbob/Models/hair_longbob_m_human.vmdl",
		$"{Clothes}/hair/hair_messy_bun/Models/messy_bun_m_human.vmdl", $"{Clothes}/hair/hair_shortscruffy/Models/hair_shortscruffy_m_human.vmdl",
		$"{Clothes}/hair/hair_slickedback/Models/hair_slickedback_m_human.vmdl", $"{Clothes}/hair/medium_beard/Models/medium_beard_m_human.vmdl",
		$"{Clothes}/hair/braided_long_beard/Models/braided_long_beard_m_human.vmdl",
	];

	static readonly string[] Shirts =
	[
		$"{Clothes}/shirt/Flannel_Shirt/Models/flannel_shirt_m_human.vmdl", $"{Clothes}/shirt/Chef_Shirt/Models/chef_shirt_m_human.vmdl",
		$"{Clothes}/shirt/Jumpsuit/Models/jumpsuit_m_human.vmdl", $"{Clothes}/shirt/Loose_Crop_Top/Models/loose_crop_top_m_human.vmdl",
	];

	static readonly string[] Trousers =
	[
		$"{Clothes}/trousers/Jeans/Models/jeans_m_human.vmdl", $"{Clothes}/trousers/CargoPants/Models/cargo_pants_m_human.vmdl",
		$"{Clothes}/trousers/LooseTrousers/Models/loose_trousers_m_human.vmdl", $"{Clothes}/trousers/TrackieBottoms/Models/trackie_bottoms_m_human.vmdl",
	];

	static readonly string[] Shoes =
	[
		$"{Clothes}/shoes/Boots/Models/boots_m_human.vmdl", $"{Clothes}/shoes/Boots/Models/army_boots_m_human.vmdl",
		$"{Clothes}/shoes/FlatShoes/Models/flat_shoes_m_human.vmdl",
	];

	static readonly string[] Jackets =
	[
		$"{Clothes}/jacket/Hoodie/Models/hoodie_m_human.vmdl", $"{Clothes}/jacket/Bomber_Jacket/Models/bomber_jacket_m_human.vmdl",
		$"{Clothes}/jacket/Puffer_Jacket/Models/puffer_jacket_m_human.vmdl", $"{Clothes}/jacket/Labcoat/Models/labcoat_m_human.vmdl",
		$"{Clothes}/jacket/Winter_Coat/Models/winter_coat_m_human.vmdl",
	];

	/// <summary>Standing about: full-body sequences the human has, looped.</summary>
	static readonly string[] Sequences = ["AvatarMenu_FidgetLarge_03", "AvatarMenu_ExamineLegs_02", "AvatarMenu_Entry_Wave_01", "AvatarMenu_Entry_TurnAround"];

	/// <summary>One citizen: its body and clothes in Borealis's world, the native scene model posing them, and its face's morphs.</summary>
	sealed class Citizen
	{
		public MeshObject Body;
		public (MeshObject Object, int[] FromBody, Transform[] Bones, Model Model)[] Worn;
		public SceneModel Animator;
		public BackendMorphs Morphs;
		public string[] MorphNames;
		public Vector3 Home;
		public float Yaw, WalkRadius, Phase;
		public Transform[] Bones;
	}

	sealed record Variant( string Name, bool Parallel );

	struct Frame
	{
		public double Animate, Render, Copy, Gpu;
	}

	public static int Run( Source2Backend backend )
	{
		var width = Args.Int( "-width", 1280 );
		var height = Args.Int( "-height", 720 );
		var samples = Args.Int( "-msaa", 4 );
		var frames = Math.Max( Args.Int( "-frames", 600 ), 10 );
		var warmup = Math.Max( Args.Int( "-warmup", 120 ), 1 );
		var count = Math.Max( Args.Int( "-count", 240 ), 1 );
		var output = Path.GetFullPath( Args.String( "-out" ) ?? Path.Combine( "screenshots", "borealislab", "crowd" ) );
		Variant[] all = [new( "serial", false ), new( "parallel", true )];
		var only = Args.String( "-variants" )?.Split( ',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries );
		var variants = only is null ? all : all.Where( v => only.Contains( v.Name, StringComparer.OrdinalIgnoreCase ) ).ToArray();

		var errors = 0;
		using var window = new SdlWindow( new SdlWindowOptions { Title = "Borealis Lab: Crowd Near on Borealis.Gpu", Width = width, Height = height } );
		using var gpu = GpuDevice.Create( new DeviceOptions
		{
			ApplicationName = "Sbox",
			EngineName = "Source2",
			Validation = Args.Has( "-validation" ),
			InstanceExtensions = Sdl.VulkanInstanceExtensions,
			Message = m =>
			{
				if ( m.Severity < MessageSeverity.Warning ) return;
				if ( m.Severity == MessageSeverity.Error ) Interlocked.Increment( ref errors );
				if ( errors < 20 ) Console.WriteLine( $"[{m.Severity}] {m.Id}: {m.Text}" );
			},
		} );

		var (clientWidth, clientHeight) = window.PixelSize;
		using var swapChain = gpu.CreateSwapChain( new SwapChainInfo
		{
			Surface = SurfaceSource.Custom( window.CreateVulkanSurface ),
			Width = clientWidth,
			Height = clientHeight,
			PresentMode = Args.Has( "-vsync" ) ? PresentMode.Fifo : PresentMode.Immediate,
		} );

		using var device = new BackendDevice( gpu );
		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( device );
		var animationWorld = new SceneWorld();
		var system = new RenderSystem();
		var target = ViewTarget.Create( new Vector2Int( width, height ), samples, TextureFormat.RGBA16161616F, system.DepthFormat, "Crowd" );

		try
		{
			var load = Stopwatch.StartNew();
			var (world, citizens) = Build( device, animationWorld, count );
			Console.WriteLine( $"{gpu.Adapter}, {count} citizens, {citizens.Sum( c => c.Worn.Length )} clothing models worn, loaded in {load.Elapsed.TotalSeconds:0.0} s" );

			var eye = new Vector3( -760, -120, 64 );
			var focus = new Vector3( 0, 0, 55 );
			var view = new RenderView { Name = "Crowd", FieldOfView = 70, ZNear = 5, ZFar = 12000, ClearColor = new Color( 0.08f, 0.09f, 0.11f ), Viewport = new Rect( 0, 0, width, height ) };

			using var timestamps = gpu.CreateTimestamps( 2 * 4 );
			var period = gpu.Capabilities.TimestampPeriod / 1e6;
			var ticks = new ulong[2];
			long frameNumber = 0;
			var step = 0;

			// The window's title shows the frames' stats live, as the game window's does on Borealis.Gpu
			var title = new BorealisGpuTitle();
			Action<string> setTitle = text => window.Title = text;

			Frame RenderFrame()
			{
				var f = default( Frame );
				var t0 = Stopwatch.GetTimestamp();
				Animate( citizens, step++, view, eye, focus );
				var t1 = Stopwatch.GetTimestamp();

				device.BeginFrame();
				if ( !swapChain.Acquire( out var image ) ) image = default;
				var slot = (int)(frameNumber % 4);
				device.FrameStartTimestamp = (timestamps, slot * 2);
				var t2 = Stopwatch.GetTimestamp();

				RenderHost.Current.NextFrame();
				system.Render( world, view, target );
				var t3 = Stopwatch.GetTimestamp();

				var copy = gpu.BeginCommandList( QueueType.Graphics, "Crowd to window" );
				if ( image.IsValid ) CopyTo( gpu, copy, target, image );
				copy.WriteTimestamp( timestamps, slot * 2 + 1 );
				gpu.Submit( copy, swapChain: image.IsValid ? swapChain : null );
				if ( image.IsValid ) swapChain.Present();
				var t4 = Stopwatch.GetTimestamp();

				f.Gpu = double.NaN;
				var old = (int)((frameNumber + 1) % 4);
				if ( frameNumber >= 4 && timestamps.TryRead( old * 2, ticks ) ) f.Gpu = (ticks[1] - ticks[0]) * period;
				frameNumber++;
				f.Animate = Ms( t0, t1 );
				f.Render = Ms( t2, t3 );
				f.Copy = Ms( t3, t4 );
				title.Frame( "Borealis Lab: Crowd Near", Sandbox.Rendering.ManagedFrameCounters.From( system.Stats ), f.Gpu, device, eye, setTitle );
				return f;
			}

			var lines = new List<string>();
			var csv = new StringBuilder( "variant,frames,animate_ms,render_ms,copy_ms,cpu_ms,frame_ms,gpu_ms,draws,depth_draws,shadow_draws,objects_visible,triangles,cpu_mean_ms,frame_mean_ms,gpu_mean_ms\n" );
			var captured = false;
			foreach ( var variant in variants )
			{
				system.ParallelRecording = variant.Parallel;
				step = 0;
				var warm = Stopwatch.StartNew();
				for ( int i = 0; (i < warmup || warm.Elapsed.TotalSeconds < 2) && !window.CloseRequested; i++ )
				{
					Maps.MapRun.Pump( window );
					RenderFrame();
				}

				// Each run measures the same stretch of the loop, as SceneLab's rewind does
				step = 0;
				var measured = new List<Frame>( frames );
				var frameTimes = new List<double>( frames );
				var last = Stopwatch.GetTimestamp();
				for ( int i = 0; i < frames && !window.CloseRequested; i++ )
				{
					Maps.MapRun.Pump( window );
					measured.Add( RenderFrame() );
					var now = Stopwatch.GetTimestamp();
					frameTimes.Add( Stopwatch.GetElapsedTime( last, now ).TotalMilliseconds );
					last = now;

					if ( i == 0 && !captured && Args.String( "-capture" ) is { } path )
					{
						captured = true;
						gpu.WaitIdle();
						var (texture, layout) = BackendDevice.Unwrap( target.Samples > 1 ? target.Resolved : target.Color );
						Capture.Save( gpu, texture, layout, Path.GetFullPath( path ) );
						BackendDevice.SetLayout( target.Samples > 1 ? target.Resolved : target.Color, ImageLayout.CopySource );
						last = Stopwatch.GetTimestamp();
					}
				}

				double Median( Func<Frame, double> f ) => Percentile( measured.Select( f ).Where( x => !double.IsNaN( x ) ), 0.5 );
				double Mean( Func<Frame, double> f ) => measured.Select( f ).Where( x => !double.IsNaN( x ) ).DefaultIfEmpty( double.NaN ).Average();
				var s = system.Stats;
				var line = $"{variant.Name,-9} animate {Median( f => f.Animate ),6:0.000} render {Median( f => f.Render ),6:0.000} copy {Median( f => f.Copy ),5:0.000} cpu {Median( f => f.Render + f.Copy ),6:0.000} (mean {Mean( f => f.Render + f.Copy ):0.000}) frame {Percentile( frameTimes, 0.5 ),6:0.000} (mean {frameTimes.DefaultIfEmpty( double.NaN ).Average():0.000}) gpu {Median( f => f.Gpu ),6:0.000} (mean {Mean( f => f.Gpu ):0.000})  "
					+ $"{s.Draws}+{s.DepthDraws} depth+{s.ShadowDraws} shadow draws, {s.ObjectsVisible} objects visible, {s.Triangles / 1e6:0.00}M tris";
				Console.WriteLine( line );
				lines.Add( line );
				csv.AppendLine( FormattableString.Invariant( $"{variant.Name},{measured.Count},{Median( f => f.Animate ):0.0000},{Median( f => f.Render ):0.0000},{Median( f => f.Copy ):0.0000},{Median( f => f.Render + f.Copy ):0.0000},{Percentile( frameTimes, 0.5 ):0.0000},{Median( f => f.Gpu ):0.0000},{s.Draws},{s.DepthDraws},{s.ShadowDraws},{s.ObjectsVisible},{s.Triangles},{Mean( f => f.Render + f.Copy ):0.0000},{frameTimes.DefaultIfEmpty( double.NaN ).Average():0.0000},{Mean( f => f.Gpu ):0.0000}" ) );
			}

			gpu.WaitIdle();
			Directory.CreateDirectory( output );
			var md = new StringBuilder();
			md.AppendLine( $"Borealis Lab -crowd, {DateTime.Now:yyyy-MM-dd HH:mm}, {gpu.Adapter.Name}, {width}x{height} {samples}x MSAA, vsync off, {count} citizens, {frames} measured frames per run (medians and means, ms)" );
			md.AppendLine();
			md.AppendLine( "```" );
			foreach ( var line in lines ) md.AppendLine( line );
			md.AppendLine( "```" );
			File.WriteAllText( Path.Combine( output, "crowd.md" ), md.ToString() );
			File.WriteAllText( Path.Combine( output, "crowd.csv" ), csv.ToString() );
			Console.WriteLine( $"Results in {output}; validation errors {gpu.ValidationErrors + errors}" );
			return gpu.ValidationErrors > 0 || errors > 0 ? 1 : 0;
		}
		finally
		{
			gpu.WaitIdle();
			target.Dispose();
			system.Dispose();
			animationWorld.Delete();
			SceneWorld.FlushQueuedDeletes();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>The plaza: a grey floor under the sun, and the citizens where SceneLab's crowd puts them, from its seed.</summary>
	static (RenderWorld World, Citizen[] Citizens) Build( BackendDevice device, SceneWorld animationWorld, int count )
	{
		var world = new RenderWorld();
		world.Lighting.SunDirection = Rotation.LookAt( new Vector3( -0.4f, 0.7f, -0.55f ) ).Forward;
		world.Lighting.SunColor = new Color( 1.0f, 0.95f, 0.85f ) * 1.5f;
		world.Lighting.AmbientColor = new Color( 0.12f, 0.14f, 0.18f );

		if ( device.LoadModel( "models/dev/plane.vmdl" ) is { } plane )
		{
			var gray = device.LoadMaterial( "materials/dev/gray_50.vmat" );
			var floorMesh = plane.WithDraws( plane.Draws.Select( d => new RenderMesh.DrawState( d.Mesh, d.DrawCall, gray, true, false, gray.ShadowFastPath ) ).ToArray() );
			world.Add( new MeshObject( floorMesh, new Transform( new Vector3( 0, 0, -1 ), Rotation.Identity, new Vector3( 60, 60, 1 ) ) ) );
		}

		var engineHuman = Model.Load( Human );
		var body = device.LoadModel( Human ) ?? throw new InvalidOperationException( $"{Human} isn't built" );
		if ( Args.String( "-crowd-eyes" ) is { } eyes )
		{
			var eyeMaterial = device.LoadMaterial( eyes ) ?? throw new InvalidOperationException( $"{eyes} isn't built" );
			var states = new List<RenderMesh.DrawState>();
			for ( int lod = 0; lod < body.LodCount; lod++ )
			{
				foreach ( var draw in body.DrawsForLod( lod ) )
				{
					if ( draw.Material?.Name.Contains( "/eyes_", StringComparison.OrdinalIgnoreCase ) == true )
						states.Add( new RenderMesh.DrawState( draw.Mesh, draw.DrawCall, eyeMaterial, true, false, eyeMaterial.ShadowFastPath ) );
				}
			}

			Console.WriteLine( $"-crowd-eyes: {states.Count} eye draws in {eyes}" );
			body = body.WithDraws( states.ToArray() );
		}

		var meshes = new Dictionary<string, (RenderMesh Mesh, Model Model)>();
		(RenderMesh, Model) Clothing( string path )
		{
			if ( meshes.TryGetValue( path, out var found ) ) return found;
			var model = Model.Load( path );
			var mesh = model is null || model.IsError ? null : device.LoadModel( path );
			return meshes[path] = (mesh, model);
		}

		var bodyBones = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase );
		for ( int b = 0; b < engineHuman.BoneCount; b++ ) bodyBones.TryAdd( engineHuman.GetBoneName( b ), b );
		var names = engineHuman.Morphs?.Names ?? [];

		var random = new Random( 4242 );
		var citizens = new Citizen[count];
		for ( int i = 0; i < count; i++ )
		{
			var angle = random.NextSingle() * MathF.Tau;
			var distance = MathF.Sqrt( random.NextSingle() ) * Extent;
			var home = new Vector3( MathF.Cos( angle ) * distance, MathF.Sin( angle ) * distance, 0 );
			var walk = i % 3 == 0 ? 40 + random.NextSingle() * 80 : 0;
			var yaw = random.NextSingle() * 360;

			// An outfit: hair, a shirt, trousers, shoes, often a jacket
			var outfit = new List<string> { Pick( random, Hair ), Pick( random, Shirts ), Pick( random, Trousers ), Pick( random, Shoes ) };
			if ( random.Next( 3 ) != 0 ) outfit.Add( Pick( random, Jackets ) );

			var transform = new Transform( home, Rotation.FromYaw( yaw ) );
			var animator = new SceneModel( animationWorld, engineHuman, transform ) { UseAnimGraph = false, PlaybackRate = 0 };
			animator.CurrentSequence.Name = Sequences[i % Sequences.Length];
			animator.Update( 0 );

			var morphs = new BackendMorphs( device );
			var citizen = new Citizen
			{
				Body = new MeshObject( body, transform ) { MorphSource = morphs },
				Animator = animator,
				Morphs = morphs,
				MorphNames = names.Length == 0 ? [] : Enumerable.Range( 0, 4 ).Select( x => names[(i * 7 + x * 13) % names.Length] ).ToArray(),
				Home = home,
				Yaw = yaw,
				WalkRadius = walk,
				Phase = random.NextSingle(),
				Bones = new Transform[engineHuman.BoneCount],
			};

			var worn = new List<(MeshObject, int[], Transform[], Model)>();
			foreach ( var path in outfit )
			{
				var (mesh, model) = Clothing( path );
				if ( mesh is null ) continue;
				var map = new int[model.BoneCount];
				for ( int b = 0; b < map.Length; b++ ) map[b] = bodyBones.TryGetValue( model.GetBoneName( b ), out var match ) ? match : -1;
				var obj = new MeshObject( mesh, transform );
				worn.Add( (obj, map, new Transform[model.BoneCount], model) );
				world.Add( obj );
			}

			citizen.Worn = worn.ToArray();
			world.Add( citizen.Body );
			citizens[i] = citizen;
		}

		return (world, citizens);
	}

	static string Pick( Random random, string[] list ) => list[random.Next( list.Length )];

	/// <summary>
	/// A frame of the crowd, as SceneLab's <c>CrowdScene.Animate</c> moves it: walkers on their circles, everyone's
	/// sequence a sixtieth of a second on, faces moving, the camera swaying. Clothing bones follow the body's by name.
	/// </summary>
	static void Animate( Citizen[] citizens, int frame, RenderView view, Vector3 eye, Vector3 focus )
	{
		var t = frame % LoopFrames / 60.0f;
		for ( int i = 0; i < citizens.Length; i++ )
		{
			var c = citizens[i];
			var transform = new Transform( c.Home, Rotation.FromYaw( c.Yaw ) );
			if ( c.WalkRadius > 0 )
			{
				var direction = i % 2 == 0 ? 1 : -1;
				var angle = direction * t * MathF.Tau / (LoopFrames / 60.0f) + i;
				var offset = new Vector3( MathF.Cos( angle ), MathF.Sin( angle ), 0 ) * c.WalkRadius;
				var velocity = new Vector3( -MathF.Sin( angle ), MathF.Cos( angle ), 0 ) * direction;
				transform = new Transform( c.Home + offset, Rotation.LookAt( velocity ) );
			}

			// Sequences loop every four seconds, each citizen from its own start
			c.Animator.Transform = transform;
			c.Animator.CurrentSequence.TimeNormalized = (c.Phase + frame / 240.0f) % 1;
			c.Animator.Update( 0 );
			c.Animator.GetBoneWorldTransforms( c.Bones );
			c.Body.Transform = transform;
			c.Body.SetBones( c.Bones );

			var bounds = c.Animator.Bounds;
			var local = new BBox( bounds.Mins - transform.Position, bounds.Maxs - transform.Position );
			c.Body.LocalBounds = local;

			foreach ( var (obj, map, bones, model) in c.Worn )
			{
				for ( int b = 0; b < bones.Length; b++ )
				{
					if ( map[b] >= 0 )
					{
						bones[b] = c.Bones[map[b]];
						continue;
					}

					var parent = model.GetBoneParent( b );
					var bind = model.GetBoneTransform( b );
					bones[b] = parent >= 0 && parent < b ? bones[parent].ToWorld( model.GetBoneTransform( parent ).ToLocal( bind ) ) : transform.ToWorld( bind );
				}

				obj.Transform = transform;
				obj.SetBones( bones );
				obj.LocalBounds = local;
			}

			for ( int m = 0; m < c.MorphNames.Length; m++ )
			{
				var weight = MathF.Max( 0, MathF.Sin( t * (1.5f + m * 0.7f) + i * 0.37f + m ) );
				c.Morphs.Set( c.MorphNames[m], weight * 0.8f );
			}
		}

		var sway = MathF.Sin( t * MathF.Tau / (LoopFrames / 60.0f) ) * (eye - focus).Length * 0.08f;
		var side = Rotation.LookAt( focus - eye ).Right;
		view.Position = eye + side * sway;
		view.Rotation = Rotation.LookAt( focus - view.Position );
	}

	/// <summary>Copy the resolved colour into a swap chain image, left ready to present.</summary>
	internal static void CopyTo( GpuDevice gpu, CommandList list, ViewTarget target, TextureHandle image )
	{
		var resolved = target.Samples > 1 ? target.Resolved : target.Color;
		var (texture, layout) = BackendDevice.Unwrap( resolved );
		list.Barrier( GpuTextureBarrier.Transition( texture, layout, ImageLayout.CopySource, PipelineStage.AllCommands, MemoryAccess.MemoryWrite, PipelineStage.Transfer, MemoryAccess.TransferRead ) );
		list.Barrier( GpuTextureBarrier.Transition( image, ImageLayout.Undefined, ImageLayout.CopyDestination, PipelineStage.Transfer, MemoryAccess.None, PipelineStage.Transfer, MemoryAccess.TransferWrite ) );
		var info = gpu.InfoOf( texture );
		var size = gpu.InfoOf( image );
		list.Blit( texture, (0, 0, info.Width, info.Height), image, (0, 0, size.Width, size.Height), Filter.Linear );
		list.Barrier( GpuTextureBarrier.Transition( image, ImageLayout.CopyDestination, ImageLayout.Present, PipelineStage.Transfer, MemoryAccess.TransferWrite, PipelineStage.BottomOfPipe, MemoryAccess.None ) );
		BackendDevice.SetLayout( resolved, ImageLayout.CopySource );
	}

	static double Ms( long from, long to ) => Stopwatch.GetElapsedTime( from, to ).TotalMilliseconds;

	static double Percentile( IEnumerable<double> values, double p )
	{
		var sorted = values.OrderBy( x => x ).ToArray();
		return sorted.Length == 0 ? double.NaN : sorted[Math.Clamp( (int)(p * (sorted.Length - 1) + 0.5), 0, sorted.Length - 1 )];
	}
}
