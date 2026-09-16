using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Sandbox.UI;
using System;
using System.IO;
using System.Reflection;

namespace UITests.Panels;

public partial class PanelScrollbarTest
{
	[TestMethod]
	[DataRow( 1.0f )]
	[DataRow( 1.75f )]
	public void AddonPanelCanControlScrollBoundsWithoutScrollbarAccess( float scale )
	{
		// Compile outside the test friend assembly so this cannot accidentally depend on engine internals.
		var source = """
			using Sandbox;
			using Sandbox.UI;

			public sealed class AddonScrollPanel : Panel
			{
				Vector2 _contentSize;
				public Vector2 ContentSize
				{
					get => _contentSize;
					set
					{
						if ( _contentSize == value ) return;
						_contentSize = value;
						SetNeedsFinalLayout();
					}
				}

				public Panel Cell { get; }

				public AddonScrollPanel()
				{
					Cell = AddChild<Panel>();
					Cell.Style.Set( "position: absolute; width: 100px; height: 100px;" );
				}

				protected override void FinalLayoutChildren( Vector2 offset )
				{
					Cell.FinalLayout( offset );
					ConstrainScrolling( Vector2.Max( Box.Rect.Size, ContentSize * ScaleToScreen ) );
				}
			}
			""";
		var references = ((string)AppContext.GetData( "TRUSTED_PLATFORM_ASSEMBLIES" )).Split( Path.PathSeparator )
			.Select( path => MetadataReference.CreateFromFile( path ) );
		var compilation = CSharpCompilation.Create( "AddonScrollLayoutTest", [CSharpSyntaxTree.ParseText( source )], references,
			new CSharpCompilationOptions( OutputKind.DynamicallyLinkedLibrary ) );
		using var output = new MemoryStream();
		var result = compilation.Emit( output );
		Assert.IsTrue( result.Success, string.Join( Environment.NewLine, result.Diagnostics ) );
		var type = Assembly.Load( output.ToArray() ).GetType( "AddonScrollPanel" );
		var scroller = (Panel)Activator.CreateInstance( type );
		var contentSize = type.GetProperty( "ContentSize" );
		var cell = (Panel)type.GetProperty( "Cell" ).GetValue( scroller );
		var root = new ScaledRoot( scale ) { PanelBounds = new Rect( 0, 0, 2000, 2000 ) };

		try
		{
			scroller.Parent = root;
			scroller.Style.Set( "position: relative; width: 200px; height: 200px; overflow: scroll; scrollbar-width: 8px; pointer-events: all;" );
			contentSize.SetValue( scroller, new Vector2( 400, 1000 ) );
			Settle( root );

			Assert.AreEqual( new Vector2( 200, 800 ) * scale, scroller.ScrollSize );
			Assert.AreEqual( 192 * scale, VerticalBar( scroller ).Box.Rect.Height, 0.001f );
			Assert.AreEqual( 192 * scale, HorizontalBar( scroller ).Box.Rect.Width, 0.001f );

			scroller.ScrollTo( new Vector2( 100, 400 ) * scale );
			Settle( root );
			Assert.AreEqual( -400 * scale, cell.Box.Rect.Top, 0.001f );
			Assert.AreEqual( -100 * scale, cell.Box.Rect.Left, 0.001f );
			Assert.AreEqual( 0, VerticalBar( scroller ).Box.Rect.Top, 0.001f );
			var thumb = Thumb( VerticalBar( scroller ) );
			AssertPicked( root, thumb.Box.Rect.Center, thumb );

			// Only the custom bounds change: no style or child mutation can trigger layout for us.
			contentSize.SetValue( scroller, new Vector2( 100, 100 ) );
			Settle( root );
			Assert.AreEqual( Vector2.Zero, scroller.ScrollSize );
			Assert.AreEqual( Vector2.Zero, scroller.ScrollOffset );
			Assert.IsNull( VerticalBar( scroller ) );
			Assert.IsNull( HorizontalBar( scroller ) );

			contentSize.SetValue( scroller, new Vector2( 400, 1000 ) );
			scroller.Style.Set( "width: 300px; height: 240px; scrollbar-width: 12px;" );
			Settle( root );
			Assert.AreEqual( new Vector2( 100, 760 ) * scale, scroller.ScrollSize );
			Assert.AreEqual( 228 * scale, VerticalBar( scroller ).Box.Rect.Height, 0.001f );
			Assert.AreEqual( 288 * scale, HorizontalBar( scroller ).Box.Rect.Width, 0.001f );
		}
		finally
		{
			root.Delete( true );
		}
	}
}
