using Borealis;
using Borealis.Backend;
using Borealis.Gpu;
using Borealis.Source2;
using Sandbox;
using System.IO;
using System.Threading;

namespace BorealisGpuTests;

/// <summary>
/// The visibility ids views (<see cref="IdsViewLayer"/>; docs/reference/borealis.md, "Visibility ids views"): each pixel's
/// id as a colour by its triangle, instance or draw, over the unlit frame, and what's said where a view has none.
/// </summary>
[TestClass]
[DoNotParallelize]
public class IdsViewTests : RenderGpuTest
{
	const int Width = 320, Height = 200;

	/// <summary>
	/// The ids views map to their modes, draw shaders unlit underneath and take no tools pass.
	/// </summary>
	[TestMethod]
	public void ModesMapToIdsViews()
	{
		var view = new RenderView();
		foreach ( var (mode, ids) in ((int, IdsView)[])[(ToolsViews.VisibilityTriangles, IdsView.Triangles), (ToolsViews.VisibilityInstances, IdsView.Instances), (ToolsViews.VisibilityDraws, IdsView.Draws)] )
		{
			view.ToolsVisMode = mode;
			Assert.AreEqual( ids, ToolsViews.IdsViewOf( view ) );
			Assert.AreEqual( ToolsViews.FullBright, ToolsViews.ShaderMode( view ), "unlit underneath" );
			Assert.AreEqual( ToolsPass.None, ToolsViews.PassOf( view ) );
		}

		view.ToolsVisMode = ToolsViews.ShaderIds;
		Assert.AreEqual( IdsView.None, ToolsViews.IdsViewOf( view ) );
	}

	/// <summary>
	/// The five boxes of one aggregate draw their ids: the pixels on them are opaque, in more colours than one per box (a
	/// colour per triangle's first corner), and pixels on no box are left for the frame to show through. Showing them makes
	/// the ids two words with or without <c>-ids-normals</c>, so the instances view has a colour per box and the draws view
	/// draws too. Once no view shows them, the ids go back to one word.
	/// </summary>
	[TestMethod]
	[DataRow( 1, false )]
	[DataRow( 4, false )]
	[DataRow( 4, true )]
	public void IdsDrawAsColours( int samples, bool twoWords )
	{
		var core = ContentFiles.FindGame().Roots.FirstOrDefault();
		if ( core is not null ) Borealis.Backend.Meshlets.MeshOptimizer.Directory = EngineFolders.NativeIn( Path.GetDirectoryName( core ) );

		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true, GpuDriven = true, GpuDrivenVisibility = true, IdsNormals = twoWords };
		GpuDrivenTests.RequireGpuDrivenDepth( backend );
		if ( backend.IdsViewShader is null ) Assert.Inconclusive( "borealis_ids_view_cs isn't built, or pass modules are off" );

		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var system = new RenderSystem { ParallelRecording = false };
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), samples, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Ids view test" );
			var (world, view, _, _) = IdsNormalsTests.BoxScene( backend );

			IdsViewLayer layer = null;
			byte[] Frame( IdsView mode )
			{
				view.ToolsVisMode = mode switch { IdsView.Triangles => ToolsViews.VisibilityTriangles, IdsView.Instances => ToolsViews.VisibilityInstances, _ => ToolsViews.VisibilityDraws };
				backend.BeginFrame();
				RenderHost.Current.NextFrame();
				system.Render( world, view, target );
				Device.WaitIdle();
				layer ??= system.Layers.OfType<IdsViewLayer>().First();
				if ( layer.LastDrawn is null ) return null;
				var (texture, layout) = BackendDevice.Unwrap( layer.LastDrawn );
				var pixels = ReadBack( texture, layout );
				BackendDevice.SetLayout( layer.LastDrawn, ImageLayout.CopySource );
				return pixels;
			}

			(int Opaque, int Clear, int Colours) Count( byte[] pixels )
			{
				var (opaque, clear) = (0, 0);
				var colours = new HashSet<int>();
				for ( int i = 0; i < Width * Height; i++ )
				{
					if ( pixels[i * 4 + 3] == 255 )
					{
						opaque++;
						colours.Add( pixels[i * 4] | pixels[i * 4 + 1] << 8 | pixels[i * 4 + 2] << 16 );
					}
					else clear++;
				}

				return (opaque, clear, colours.Count);
			}

			// The ids targets are made for the view's next frame, and the whole prepass kept from the frame after
			byte[] triangles = null;
			for ( int i = 0; i < 6; i++ ) triangles = Frame( IdsView.Triangles );

			Assert.AreEqual( IdsViewResult.Drawn, IdsViewLayer.LastResult );
			Assert.IsNotNull( triangles, "the layer drew the ids" );
			var (opaque, clear, colours) = Count( triangles );
			Assert.IsTrue( opaque > 2000, $"the boxes cover the frame ({opaque} pixels with ids)" );
			Assert.IsTrue( clear > 2000, $"pixels on no box show the frame through ({clear})" );
			Assert.IsTrue( colours >= 3, $"a colour per triangle's first corner, which the boxes' visible faces share ({colours} colours)" );

			Assert.AreEqual( Format.RG32Uint, backend.IdsFormat, "two-word ids while a view shows them" );
			var instances = Frame( IdsView.Instances );
			Assert.AreEqual( IdsViewResult.Drawn, IdsViewLayer.LastResult );
			var (boxes, _, boxColours) = Count( instances );
			Assert.IsTrue( boxes > 2000 );
			Assert.IsTrue( boxColours is >= 4 and <= 6, $"a colour per box ({boxColours} colours)" );

			var draws = Frame( IdsView.Draws );
			Assert.AreEqual( IdsViewResult.Drawn, IdsViewLayer.LastResult );
			var (drawn, _, drawColours) = Count( draws );
			Assert.IsTrue( drawn > 2000 && drawColours is >= 1 and <= 5, $"a colour per draw record ({drawColours} colours over {drawn} pixels)" );

			// Lit again: a few frames on, the ids are one word again unless -ids-normals keeps them two
			view.ToolsVisMode = 0;
			for ( int i = 0; i < 4; i++ )
			{
				backend.BeginFrame();
				RenderHost.Current.NextFrame();
				system.Render( world, view, target );
				Device.WaitIdle();
			}

			Assert.AreEqual( twoWords ? Format.RG32Uint : Format.R32Uint, backend.IdsFormat );

			Assert.AreEqual( 0, Device.ValidationErrors, "no validation errors" );
		}
		finally
		{
			// Each ids format's pipelines are made on workers: done before the backend's shaders go
			Device.WaitForPendingPipelines( TimeSpan.FromSeconds( 30 ) );
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}
}
