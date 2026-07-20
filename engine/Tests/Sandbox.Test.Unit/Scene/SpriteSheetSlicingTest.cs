using System.Collections.Generic;
using System.Linq;

namespace SceneTests;

/// <summary>
/// Slicing turns an image into the named, individually-pivoted parts a cutout rig is built from.
/// It is worth testing hard because every mistake here is silent: a rect a few pixels out still
/// renders, an island missed by automatic detection just looks like the artist forgot to draw it,
/// and a re-slice that loses ids breaks sprites that were fine a moment ago.
/// </summary>
[TestClass]
public class SpriteSheetSlicingTest
{
	static Color32 Opaque => new Color32( 255, 255, 255, 255 );

	static Color32[] Blank( int width, int height ) => new Color32[width * height];

	/// <summary>Fill a pixel run, in the same top-left-origin space slices use.</summary>
	static void Fill( Color32[] pixels, int width, int x, int y, int w, int h )
	{
		for ( int py = y; py < y + h; py++ )
		{
			for ( int px = x; px < x + w; px++ )
			{
				pixels[(py * width) + px] = Opaque;
			}
		}
	}

	static void AssertRect( Rect rect, float x, float y, float w, float h, string because = null )
	{
		Assert.AreEqual( x, rect.Left, $"left {because}" );
		Assert.AreEqual( y, rect.Top, $"top {because}" );
		Assert.AreEqual( w, rect.Width, $"width {because}" );
		Assert.AreEqual( h, rect.Height, $"height {because}" );
	}

	//
	// Grids
	//

	[TestMethod]
	public void GridByCellCountDividesTheImageEvenly()
	{
		var settings = new SpriteSheet.SliceSettings
		{
			Type = SpriteSheet.SliceType.GridByCellCount,
			CellCount = new Vector2Int( 4, 2 ),
			KeepEmptyRects = true
		};

		var rects = SpriteSheet.SliceGridByCellCount( Blank( 64, 32 ), 64, 32, settings );

		Assert.AreEqual( 8, rects.Count );
		foreach ( var rect in rects )
		{
			Assert.AreEqual( 16, rect.Width );
			Assert.AreEqual( 16, rect.Height );
		}

		// Reading order: across the top row first, then down.
		AssertRect( rects[0], 0, 0, 16, 16, "first cell" );
		AssertRect( rects[3], 48, 0, 16, 16, "end of the top row" );
		AssertRect( rects[4], 0, 16, 16, 16, "start of the second row" );
	}

	[TestMethod]
	public void GridByCellCountTakesOffsetAndPaddingBeforeDividing()
	{
		// 64 wide, 4 across, starting 4px in, with 2px between cells:
		//   (64 - 4 - 2*3) / 4 = 13.5 -> 13
		var settings = new SpriteSheet.SliceSettings
		{
			Type = SpriteSheet.SliceType.GridByCellCount,
			CellCount = new Vector2Int( 4, 1 ),
			Offset = new Vector2Int( 4, 0 ),
			Padding = new Vector2Int( 2, 0 ),
			KeepEmptyRects = true
		};

		var rects = SpriteSheet.SliceGridByCellCount( Blank( 64, 16 ), 64, 16, settings );

		Assert.AreEqual( 4, rects.Count );
		AssertRect( rects[0], 4, 0, 13, 16 );
		AssertRect( rects[1], 19, 0, 13, 16, "one cell plus the gap along" );
	}

	[TestMethod]
	public void GridByCellSizeOnlyEmitsWholeCells()
	{
		// 70px of image at 16px cells leaves 6px over, which is not a cell.
		var settings = new SpriteSheet.SliceSettings
		{
			Type = SpriteSheet.SliceType.GridByCellSize,
			CellSize = new Vector2Int( 16, 16 ),
			KeepEmptyRects = true
		};

		var rects = SpriteSheet.SliceGridByCellSize( Blank( 70, 16 ), 70, 16, settings );

		Assert.AreEqual( 4, rects.Count, "the leftover strip is not a cell and must not be emitted" );
	}

	[TestMethod]
	public void EmptyGridCellsAreDroppedUnlessAskedFor()
	{
		var pixels = Blank( 32, 16 );
		Fill( pixels, 32, 0, 0, 16, 16 ); // only the left cell has anything in it

		var settings = new SpriteSheet.SliceSettings
		{
			Type = SpriteSheet.SliceType.GridByCellSize,
			CellSize = new Vector2Int( 16, 16 )
		};

		var dropped = SpriteSheet.SliceGridByCellSize( pixels, 32, 16, settings );
		Assert.AreEqual( 1, dropped.Count, "a sheet is rarely full, so blank cells go by default" );

		settings.KeepEmptyRects = true;
		var kept = SpriteSheet.SliceGridByCellSize( pixels, 32, 16, settings );
		Assert.AreEqual( 2, kept.Count );
	}

	//
	// Automatic
	//

	[TestMethod]
	public void AutomaticFindsEachIslandSeparately()
	{
		// Two blobs with a gap between them - a sheet of loose parts, which is what the modular
		// character workflow produces.
		var pixels = Blank( 64, 32 );
		Fill( pixels, 64, 2, 2, 10, 10 );
		Fill( pixels, 64, 40, 6, 12, 8 );

		var rects = SpriteSheet.SliceAutomatic( pixels, 64, 32, new SpriteSheet.SliceSettings() );

		Assert.AreEqual( 2, rects.Count );
		AssertRect( rects[0], 2, 2, 10, 10, "first blob, tight to its pixels" );
		AssertRect( rects[1], 40, 6, 12, 8, "second blob" );
	}

	[TestMethod]
	public void AutomaticIgnoresSpecksBelowTheMinimumSize()
	{
		var pixels = Blank( 32, 32 );
		Fill( pixels, 32, 1, 1, 8, 8 );  // real artwork
		Fill( pixels, 32, 20, 20, 2, 2 ); // a stray couple of pixels

		var rects = SpriteSheet.SliceAutomatic( pixels, 32, 32, new SpriteSheet.SliceSettings() );

		Assert.AreEqual( 1, rects.Count, $"anything under {SpriteSheet.MinimumAutomaticSliceSize}px is noise" );
		AssertRect( rects[0], 1, 1, 8, 8 );
	}

	[TestMethod]
	public void AutomaticFoldsTogetherIslandsWhoseBoxesOverlap()
	{
		// A hollow ring with a separate dot floating inside it. The two never touch, so they are
		// two islands, but they are plainly one piece of artwork - the dot's box sits entirely
		// inside the ring's.
		var pixels = Blank( 32, 32 );
		Fill( pixels, 32, 0, 0, 10, 1 );  // ring: top
		Fill( pixels, 32, 0, 9, 10, 1 );  //       bottom
		Fill( pixels, 32, 0, 0, 1, 10 );  //       left
		Fill( pixels, 32, 9, 0, 1, 10 );  //       right
		Fill( pixels, 32, 3, 3, 4, 4 );   // the dot inside

		var rects = SpriteSheet.SliceAutomatic( pixels, 32, 32, new SpriteSheet.SliceSettings() );

		Assert.AreEqual( 1, rects.Count, "overlapping boxes are one thing drawn in pieces" );
		AssertRect( rects[0], 0, 0, 10, 10 );
	}

	[TestMethod]
	public void AutomaticOrdersRowsLeftToRightEvenWhenTopsDiffer()
	{
		// Automatic slicing trims to the artwork, so two parts on the same row almost never share a
		// top edge. Ordering on top alone would interleave them with the row below, and frame order
		// is animation order.
		var pixels = Blank( 64, 64 );
		Fill( pixels, 64, 30, 2, 8, 8 );  // right-hand part, sits higher
		Fill( pixels, 64, 4, 5, 8, 8 );   // left-hand part, sits lower but shares the row
		Fill( pixels, 64, 4, 40, 8, 8 );  // clearly a row below

		var rects = SpriteSheet.SliceAutomatic( pixels, 64, 64, new SpriteSheet.SliceSettings() );

		Assert.AreEqual( 3, rects.Count );
		AssertRect( rects[0], 4, 5, 8, 8, "leftmost of the top row comes first despite its lower top" );
		AssertRect( rects[1], 30, 2, 8, 8, "then its neighbour" );
		AssertRect( rects[2], 4, 40, 8, 8, "then the next row down" );
	}

	[TestMethod]
	public void AlphaToleranceDecidesWhatCountsAsArtwork()
	{
		var pixels = Blank( 32, 32 );
		Fill( pixels, 32, 1, 1, 8, 8 );

		// A faint patch of nearly-transparent pixels, the sort of thing a soft brush or a bad export
		// leaves behind. Deliberately bigger than the minimum slice size, so that what decides its
		// fate is the alpha tolerance and not the noise filter.
		for ( int y = 20; y < 28; y++ )
		{
			for ( int x = 20; x < 28; x++ ) pixels[(y * 32) + x] = new Color32( 255, 255, 255, 6 );
		}

		var strict = SpriteSheet.SliceAutomatic( pixels, 32, 32, new SpriteSheet.SliceSettings() );
		Assert.AreEqual( 2, strict.Count, "by default anything above zero alpha is artwork" );

		var tolerant = SpriteSheet.SliceAutomatic( pixels, 32, 32,
			new SpriteSheet.SliceSettings { AlphaTolerance = 10 } );
		Assert.AreEqual( 1, tolerant.Count, "raising the tolerance discards the halo" );
	}

	//
	// Artwork on a solid background, which is what a JPEG or a scan actually looks like
	//

	/// <summary>Fill the whole image with a colour, the way a white-background export arrives.</summary>
	static Color32[] Filled( int width, int height, Color32 colour )
	{
		var pixels = new Color32[width * height];
		for ( int i = 0; i < pixels.Length; i++ ) pixels[i] = colour;
		return pixels;
	}

	static void FillWith( Color32[] pixels, int width, int x, int y, int w, int h, Color32 colour )
	{
		for ( int py = y; py < y + h; py++ )
		{
			for ( int px = x; px < x + w; px++ ) pixels[(py * width) + px] = colour;
		}
	}

	static Color32 White => new Color32( 255, 255, 255, 255 );
	static Color32 Ink => new Color32( 20, 30, 40, 255 );

	[TestMethod]
	public void OpaqueArtworkWithNoBackgroundKeyFindsOneBigRectangle()
	{
		// The failure this was reported as. A JPEG has no alpha channel at all, so every pixel is
		// artwork, every pixel is connected, and the whole image comes back as a single island.
		var pixels = Filled( 64, 32, White );
		FillWith( pixels, 64, 4, 4, 10, 10, Ink );
		FillWith( pixels, 64, 40, 4, 10, 10, Ink );

		var rects = SpriteSheet.SliceAutomatic( pixels, 64, 32, new SpriteSheet.SliceSettings() );

		Assert.AreEqual( 1, rects.Count );
		AssertRect( rects[0], 0, 0, 64, 32, "the entire sheet, which is the symptom to look out for" );
	}

	[TestMethod]
	public void KeyingTheBackgroundColourFindsTheArtwork()
	{
		var pixels = Filled( 64, 32, White );
		FillWith( pixels, 64, 4, 4, 10, 10, Ink );
		FillWith( pixels, 64, 40, 6, 12, 8, Ink );

		var settings = new SpriteSheet.SliceSettings
		{
			Background = new SpriteSheet.BackgroundKey { Enabled = true, Color = White, Tolerance = 8 }
		};

		var rects = SpriteSheet.SliceAutomatic( pixels, 64, 32, settings );

		Assert.AreEqual( 2, rects.Count, "with white understood as empty, the two shapes separate" );
		AssertRect( rects[0], 4, 4, 10, 10 );
		AssertRect( rects[1], 40, 6, 12, 8 );
	}

	[TestMethod]
	public void ToleranceCopesWithABackgroundThatIsNotQuiteFlat()
	{
		// JPEG never gives back the white it was handed.
		var pixels = Filled( 64, 32, new Color32( 250, 252, 249, 255 ) );
		FillWith( pixels, 64, 4, 4, 10, 10, Ink );

		var tight = new SpriteSheet.SliceSettings
		{
			Background = new SpriteSheet.BackgroundKey { Enabled = true, Color = White, Tolerance = 1 }
		};
		Assert.AreEqual( 1, SpriteSheet.SliceAutomatic( pixels, 64, 32, tight ).Count );
		AssertRect( SpriteSheet.SliceAutomatic( pixels, 64, 32, tight )[0], 0, 0, 64, 32,
			"too tight a tolerance and the off-white still reads as artwork" );

		var forgiving = new SpriteSheet.SliceSettings
		{
			Background = new SpriteSheet.BackgroundKey { Enabled = true, Color = White, Tolerance = 12 }
		};
		var rects = SpriteSheet.SliceAutomatic( pixels, 64, 32, forgiving );
		Assert.AreEqual( 1, rects.Count );
		AssertRect( rects[0], 4, 4, 10, 10, "a little slack and the drawing separates cleanly" );
	}

	[TestMethod]
	public void BackgroundColourIsGuessedFromTheCorners()
	{
		var pixels = Filled( 64, 32, White );
		FillWith( pixels, 64, 20, 10, 20, 12, Ink );

		var detected = SpriteSheet.DetectBackgroundColor( pixels, 64, 32 );

		Assert.IsNotNull( detected );
		Assert.AreEqual( 1f, detected.Value.r, 0.01f );
		Assert.AreEqual( 1f, detected.Value.g, 0.01f );
		Assert.AreEqual( 1f, detected.Value.b, 0.01f );
	}

	[TestMethod]
	public void NoGuessIsMadeWhenTheCornersDisagree()
	{
		// Artwork running into a corner, or a gradient. Guessing here would key away part of the
		// drawing, which is worse than leaving it to be set by hand.
		var pixels = Filled( 64, 32, White );
		FillWith( pixels, 64, 0, 0, 20, 20, Ink );

		Assert.IsNull( SpriteSheet.DetectBackgroundColor( pixels, 64, 32 ) );
	}

	[TestMethod]
	public void NoGuessIsMadeWhenTheImageIsAlreadyTransparent()
	{
		var pixels = Blank( 64, 32 );
		Fill( pixels, 64, 20, 10, 20, 12 );

		Assert.IsNull( SpriteSheet.DetectBackgroundColor( pixels, 64, 32 ),
			"alpha is already doing the job; keying a colour on top would only cause trouble" );
	}

	[TestMethod]
	public void TrimUsesTheBackgroundKeyToo()
	{
		// Otherwise trimming a slice on a white sheet does nothing at all, because every pixel in it
		// counts as content.
		var pixels = Filled( 64, 32, White );
		FillWith( pixels, 64, 10, 12, 5, 6, Ink );

		var key = new SpriteSheet.BackgroundKey { Enabled = true, Color = White, Tolerance = 8 };
		var trimmed = SpriteSheet.Trim( pixels, 64, 32, new Rect( 0, 0, 64, 32 ), 0, key );

		AssertRect( trimmed, 10, 12, 5, 6 );
	}

	//
	// Trim
	//

	[TestMethod]
	public void TrimTightensToTheArtwork()
	{
		var pixels = Blank( 32, 32 );
		Fill( pixels, 32, 10, 12, 5, 6 );

		var trimmed = SpriteSheet.Trim( pixels, 32, 32, new Rect( 0, 0, 32, 32 ) );

		AssertRect( trimmed, 10, 12, 5, 6 );
	}

	[TestMethod]
	public void TrimmingNothingGivesNothing()
	{
		var trimmed = SpriteSheet.Trim( Blank( 32, 32 ), 32, 32, new Rect( 0, 0, 32, 32 ) );

		Assert.AreEqual( 0, trimmed.Width, "an empty rect is the signal there was nothing to keep" );
		Assert.AreEqual( 0, trimmed.Height );
	}

	//
	// Re-slicing
	//

	static SpriteSheet.SliceSettings GridOf( int columns, int rows ) => new()
	{
		Type = SpriteSheet.SliceType.GridByCellCount,
		CellCount = new Vector2Int( columns, rows ),
		KeepEmptyRects = true
	};

	[TestMethod]
	public void EveryNewSliceGetsItsOwnIdentity()
	{
		var slices = SpriteSheet.CreateSlices( Blank( 64, 16 ), 64, 16, GridOf( 4, 1 ) );

		Assert.AreEqual( 4, slices.Count );
		Assert.AreEqual( 4, slices.Select( s => s.Id ).Distinct().Count(), "ids must be unique" );
		Assert.AreEqual( 4, slices.Select( s => s.Name ).Distinct().Count(), "names must be unique" );
		Assert.IsFalse( slices.Any( s => s.Id == System.Guid.Empty ) );
	}

	[TestMethod]
	public void DeleteExistingStartsOver()
	{
		var original = new SpriteSheet.Slice { Name = "L_Thigh", Rect = new Rect( 0, 0, 16, 16 ) };

		var settings = GridOf( 2, 1 );
		settings.Method = SpriteSheet.SliceMethod.DeleteExisting;

		var slices = SpriteSheet.CreateSlices( Blank( 32, 16 ), 32, 16, settings, new[] { original } );

		Assert.AreEqual( 2, slices.Count );
		Assert.IsFalse( slices.Any( s => s.Id == original.Id ), "nothing survives this method" );
	}

	[TestMethod]
	public void SmartReshapesTheSliceThatWasAlreadyThere()
	{
		// The case that matters: parts have been cut, renamed and had their pivots placed on joints,
		// and now the sheet is sliced again to pick up something missed. Losing the name or the id
		// would break every sprite already built from it.
		var original = new SpriteSheet.Slice
		{
			Name = "L_Thigh",
			Rect = new Rect( 0, 0, 14, 16 ),
			Pivot = new Vector2( 0.5f, 0.05f ),
			Alignment = SpriteSheet.PivotAlignment.Custom
		};
		var id = original.Id;

		var settings = GridOf( 2, 1 );
		settings.Method = SpriteSheet.SliceMethod.Smart;

		var slices = SpriteSheet.CreateSlices( Blank( 32, 16 ), 32, 16, settings, new[] { original } );

		Assert.AreEqual( 2, slices.Count, "the second cell is new, the first is the one we had" );

		var kept = slices.Single( s => s.Id == id );
		Assert.AreEqual( "L_Thigh", kept.Name, "the name has to survive" );
		AssertRect( kept.Rect, 0, 0, 16, 16, "but the shape follows the new cut" );

		// Deliberately unlike Unity, which reapplies the dialog's pivot here. Putting pivots on
		// joints is the slow part of rigging; re-slicing must not undo it.
		Assert.AreEqual( 0.05f, kept.Pivot.y, "the hand-placed pivot has to survive too" );
		Assert.AreEqual( SpriteSheet.PivotAlignment.Custom, kept.Alignment );
	}

	[TestMethod]
	public void SmartTreatsSomethingSomewhereElseAsNew()
	{
		var original = new SpriteSheet.Slice { Name = "Head", Rect = new Rect( 0, 0, 16, 16 ) };

		var settings = GridOf( 2, 1 );
		settings.Method = SpriteSheet.SliceMethod.Smart;

		var slices = SpriteSheet.CreateSlices( Blank( 32, 16 ), 32, 16, settings, new[] { original } );

		Assert.AreEqual( 2, slices.Count );
		Assert.IsTrue( slices.Any( s => s.Id == original.Id ) );
		Assert.AreEqual( 1, slices.Count( s => s.Id != original.Id ), "the far cell is a new slice" );
	}

	[TestMethod]
	public void SafeOnlyFillsEmptySpace()
	{
		var original = new SpriteSheet.Slice { Name = "Head", Rect = new Rect( 0, 0, 16, 16 ) };

		var settings = GridOf( 2, 1 );
		settings.Method = SpriteSheet.SliceMethod.Safe;

		var slices = SpriteSheet.CreateSlices( Blank( 32, 16 ), 32, 16, settings, new[] { original } );

		Assert.AreEqual( 2, slices.Count, "one kept, one added where nothing was" );

		var kept = slices.Single( s => s.Id == original.Id );
		AssertRect( kept.Rect, 0, 0, 16, 16, "the existing slice is untouched" );
	}

	[TestMethod]
	public void ReslicingDoesNotReuseNamesAlreadyTaken()
	{
		var original = new SpriteSheet.Slice { Name = "sheet_0", Rect = new Rect( 0, 0, 16, 16 ) };

		var settings = GridOf( 2, 1 );
		settings.Method = SpriteSheet.SliceMethod.Safe;

		var slices = SpriteSheet.CreateSlices( Blank( 32, 16 ), 32, 16, settings, new[] { original }, "sheet" );

		Assert.AreEqual( 2, slices.Select( s => s.Name ).Distinct().Count(),
			"the generated name has to step over the one already in use" );
	}

	//
	// Pivots
	//

	[TestMethod]
	public void PivotPresetsUseTheSameUpIsUpConventionAsSpriteOrigin()
	{
		// This is copied straight into Sprite.Animation.Origin, which a default (billboarded)
		// SpriteRenderer reads as Y-up. Getting it upside down would put every pivot on the wrong
		// end of every limb.
		Assert.AreEqual( new Vector2( 0f, 0f ), SpriteSheet.Slice.GetPivot( SpriteSheet.PivotAlignment.BottomLeft ) );
		Assert.AreEqual( new Vector2( 1f, 1f ), SpriteSheet.Slice.GetPivot( SpriteSheet.PivotAlignment.TopRight ) );
		Assert.AreEqual( new Vector2( 0.5f, 0.5f ), SpriteSheet.Slice.GetPivot( SpriteSheet.PivotAlignment.Center ) );
		Assert.AreEqual( new Vector2( 0.5f, 0f ), SpriteSheet.Slice.GetPivot( SpriteSheet.PivotAlignment.Bottom ) );
	}

	[TestMethod]
	public void PivotConvertsIntoImageSpaceTheOtherWayUp()
	{
		var slice = new SpriteSheet.Slice { Rect = new Rect( 10, 20, 100, 50 ) };

		slice.SetAlignment( SpriteSheet.PivotAlignment.BottomLeft );
		Assert.AreEqual( new Vector2( 10, 70 ), slice.PivotToImagePixels,
			"the bottom of the slice is the far edge in image space" );

		slice.SetAlignment( SpriteSheet.PivotAlignment.TopLeft );
		Assert.AreEqual( new Vector2( 10, 20 ), slice.PivotToImagePixels );

		slice.SetAlignment( SpriteSheet.PivotAlignment.Center );
		Assert.AreEqual( new Vector2( 60, 45 ), slice.PivotToImagePixels );
	}

	[TestMethod]
	public void PlacingAPivotByHandRoundTripsAndMarksItCustom()
	{
		var slice = new SpriteSheet.Slice { Rect = new Rect( 10, 20, 100, 50 ) };

		// Somewhere a joint might plausibly be - near the top edge, slightly off centre.
		slice.SetPivotFromImagePixels( new Vector2( 35, 25 ) );

		Assert.AreEqual( SpriteSheet.PivotAlignment.Custom, slice.Alignment );
		Assert.AreEqual( new Vector2( 35, 25 ), slice.PivotToImagePixels );
	}

	[TestMethod]
	public void SnappingToAPresetForgetsAHandPlacedPivot()
	{
		var slice = new SpriteSheet.Slice { Rect = new Rect( 0, 0, 10, 10 ) };
		slice.SetPivotFromImagePixels( new Vector2( 3, 7 ) );

		slice.SetAlignment( SpriteSheet.PivotAlignment.Center );

		Assert.AreEqual( new Vector2( 0.5f, 0.5f ), slice.Pivot );
		Assert.AreEqual( SpriteSheet.PivotAlignment.Center, slice.Alignment );
	}

	//
	// Rig hierarchy
	//

	static (SpriteSheet Sheet, SpriteSheet.Slice Hips, SpriteSheet.Slice Thigh, SpriteSheet.Slice Calf) Leg()
	{
		var hips = new SpriteSheet.Slice { Name = "Hips" };
		var thigh = new SpriteSheet.Slice { Name = "L_Thigh" };
		var calf = new SpriteSheet.Slice { Name = "L_Calf" };

		var sheet = new SpriteSheet
		{
			Slices = new List<SpriteSheet.Slice> { hips, thigh, calf }
		};

		Assert.IsTrue( sheet.TrySetParent( thigh, hips.Id ) );
		Assert.IsTrue( sheet.TrySetParent( calf, thigh.Id ) );

		return (sheet, hips, thigh, calf);
	}

	[TestMethod]
	public void PartsHangOffTheirParents()
	{
		var (sheet, hips, thigh, calf) = Leg();

		Assert.AreEqual( hips.Id, thigh.ParentId );
		Assert.AreEqual( calf.Id, sheet.GetChildren( thigh.Id ).Single().Id );
		Assert.AreEqual( hips.Id, sheet.GetChildren( System.Guid.Empty ).Single().Id, "only the hips are a root" );
	}

	[TestMethod]
	public void APartCannotBeItsOwnParent()
	{
		var (sheet, hips, _, _) = Leg();

		Assert.IsFalse( sheet.TrySetParent( hips, hips.Id ) );
		Assert.AreEqual( System.Guid.Empty, hips.ParentId );
	}

	[TestMethod]
	public void ParentingCannotBeMadeToLoop()
	{
		// Two clicks in the build dialog get you here: the thigh already owns the calf, so making the
		// thigh a child of the calf closes a ring. Building that would recurse until the stack ran out.
		var (sheet, hips, thigh, calf) = Leg();

		Assert.IsFalse( sheet.TrySetParent( hips, calf.Id ), "hips -> calf -> thigh -> hips" );
		Assert.AreEqual( System.Guid.Empty, hips.ParentId, "and the attempt must leave things as they were" );

		Assert.IsFalse( sheet.TrySetParent( thigh, calf.Id ) );
		Assert.AreEqual( hips.Id, thigh.ParentId );
	}

	[TestMethod]
	public void DetachingToRootIsAlwaysAllowed()
	{
		var (sheet, _, thigh, _) = Leg();

		Assert.IsTrue( sheet.TrySetParent( thigh, System.Guid.Empty ) );
		Assert.AreEqual( System.Guid.Empty, thigh.ParentId );
	}

	[TestMethod]
	public void BuildOrderPutsParentsFirst()
	{
		// A child has to have something to attach to by the time it is created.
		var (sheet, hips, thigh, calf) = Leg();

		var order = sheet.InHierarchyOrder();

		Assert.AreEqual( 3, order.Count );
		Assert.IsTrue( order.IndexOf( hips ) < order.IndexOf( thigh ) );
		Assert.IsTrue( order.IndexOf( thigh ) < order.IndexOf( calf ) );
	}

	[TestMethod]
	public void APartWhoseParentWentAwayIsStillBuilt()
	{
		// Delete a part that others hang off - by re-slicing, or from the canvas - and the orphans
		// must still turn up in the rig rather than quietly vanishing.
		var (sheet, hips, thigh, calf) = Leg();

		sheet.Slices.Remove( thigh );

		var order = sheet.InHierarchyOrder();

		Assert.AreEqual( 2, order.Count );
		CollectionAssert.Contains( order, calf, "the calf still points at a thigh that is gone" );
		CollectionAssert.Contains( order, hips );
	}

	//
	// Naming
	//

	[TestMethod]
	public void UniqueNamesStepAroundWhatIsAlreadyThere()
	{
		var sheet = new SpriteSheet
		{
			Slices = new List<SpriteSheet.Slice>
			{
				new() { Name = "Head" },
				new() { Name = "Head_1" }
			}
		};

		Assert.AreEqual( "Torso", sheet.GetUniqueName( "Torso" ) );
		Assert.AreEqual( "Head_2", sheet.GetUniqueName( "Head" ) );

		// Renaming a slice to what it is already called must not push it to Head_2.
		Assert.AreEqual( "Head", sheet.GetUniqueName( "Head", sheet.Slices[0] ) );
	}

	[TestMethod]
	public void SlicesAreFoundByIdRatherThanName()
	{
		var slice = new SpriteSheet.Slice { Name = "Thigh" };
		var sheet = new SpriteSheet { Slices = new List<SpriteSheet.Slice> { slice } };

		Assert.AreSame( slice, sheet.GetSlice( slice.Id ) );

		// The rename that makes a sheet usable must not lose the reference.
		slice.Name = "L_Thigh";
		Assert.AreSame( slice, sheet.GetSlice( slice.Id ) );
		Assert.IsNull( sheet.GetSlice( "Thigh" ) );
	}
}
