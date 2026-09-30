using System.Runtime.CompilerServices;

namespace Sandbox.Layout;

internal static partial class LayoutAlgorithm
{
	/// <summary>
	/// The algorithm keeps <see cref="LayoutResults.ComputedFlexBasis"/> across passes for a child with an explicit
	/// flex-basis: a pass with a definite main size only fills it in when it is still undefined, while a
	/// max-content pass overwrites it with the child's own size. The value later passes see therefore
	/// depends on pass order. Cached container measurements may only be reused loosely (see
	/// <see cref="CanReuseContainerMeasurement"/>) if no such basis anywhere below has changed since they
	/// were computed, so a change demotes every existing entry of the ancestors to exact-match only.
	/// </summary>
	internal static void NoteFlexBasisChange( LayoutNode child, float previousFlexBasis )
	{
		if ( Num.IsUndefined( previousFlexBasis ) || Num.OptionalEquals( previousFlexBasis, child.Layout.ComputedFlexBasis ) )
		{
			return;
		}

		for ( var node = child.Owner; node is not null; node = node.Owner )
		{
			ref var layout = ref node.Layout;
			for ( int i = 0; i < layout.NextCachedMeasurementsIndex; i++ )
			{
				layout.CachedMeasurements[i].ContentBased = false;
			}
		}
	}

	/// <summary>
	/// Counts the times <see cref="MeasureNodeWithFixedSize"/> answered a fit-content request with no room
	/// as the available size instead of the content size. A cached measurement whose computation included
	/// one anywhere in the subtree is not content based and cannot be reused under looser constraints
	/// (see <see cref="CanReuseContainerMeasurement"/>). The counter is per thread so concurrent layouts
	/// cannot taint each other's entries; comparing before and after a pass also handles nested layout calls.
	/// </summary>
	[ThreadStatic] private static int s_nonContentMeasurements;

	/// <summary>
	/// This is a wrapper around the <see cref="CalculateLayoutImpl"/> function. It determines whether the
	/// layout request is redundant and can be skipped. Returns true if layout was performed, false if skipped.
	/// <para>
	/// <paramref name="scope"/> lets a measure-only request ask for a single
	/// dimension, letting flex containers skip the cross-axis work (see <see cref="FlexLayout.Compute"/>)
	/// and answer stretch-fit requests without a cache lookup. Results are identical to a full measurement
	/// of that dimension; the other dimension is left undefined unless it was free. Containers whose subtree
	/// cannot make a fit-content result depend on the available size also answer such requests from looser
	/// cached measurements (see <see cref="TryReuseContainerMeasurement"/>). Nodes whose results are
	/// pass-order dependent (baseline alignment above or below, percentages in their own style, measure
	/// functions) are always measured on both axes with the complete pass sequence. A full-scope request
	/// goes through all cache checks.
	/// </para>
	/// </summary>
	internal static bool CalculateLayoutInternal(
		LayoutNode node,
		float availableWidth,
		float availableHeight,
		Direction ownerDirection,
		SizingMode widthSizingMode,
		SizingMode heightSizingMode,
		float ownerWidth,
		float ownerHeight,
		bool performLayout,
		int depth,
		uint generationCount,
		MeasureScope scope = MeasureScope.Both )
	{
		ref var layout = ref node.Layout;

		depth++;

		var needToVisitNode = (node.IsDirty && layout.GenerationCount != generationCount)
			|| layout.LastOwnerDirection != ownerDirection;

		// Conflicting flex bounds also depend on the effective owner's display. That can change
		// without dirtying this node (including through contents), so don't carry these entries
		// between generations. Ordinary nodes and reuse within a generation keep the fast path.
		if ( !needToVisitNode && layout.GenerationCount != generationCount && node.Style.Display == Display.Flex )
		{
			var style = node.Style;
			needToVisitNode = style.MinWidth.Resolve( ownerWidth ) > style.MaxWidth.Resolve( ownerWidth )
				|| style.MinHeight.Resolve( ownerHeight ) > style.MaxHeight.Resolve( ownerHeight );
		}

		if ( needToVisitNode )
		{
			InvalidateCaches( ref layout );
		}

		if ( scope != MeasureScope.Both )
		{
			// A request for one dimension only. Nodes whose results depend on the sequence of passes are measured
			// on both axes with the complete pass sequence: leaves (a measure function's cache uses looser
			// compatibility rules, whose answers depend on which entries
			// exist; an empty node's measurement is as cheap either way), baseline alignment anywhere above or
			// below (it reads measured sizes across the subtree), and a percentage in the node's own style
			// (it resolves against the owner size).
			if ( performLayout || node.ChildCount == 0 || layout.BaselineSensitive || node.NeedsExactPasses )
			{
				scope = MeasureScope.Both;
			}
			else if ( IsAnsweredByAvailableSize( node, scope, widthSizingMode, heightSizingMode, generationCount ) )
			{
				// A single stretch-fit dimension is just the bounded available size; that is cheaper than
				// searching the cache and not worth a cache slot.
				MeasureFromAvailableSize( node, availableWidth, availableHeight, ownerDirection, widthSizingMode, heightSizingMode, ownerWidth, ownerHeight );
				layout.LastOwnerDirection = ownerDirection;
				layout.GenerationCount = generationCount;
				return true;
			}
		}

		// -1 = none, -2 = cachedLayout, >= 0 = cachedMeasurements[i]
		int cachedResults = -1;

		// Determine whether the results are already cached. We maintain a separate cache for layouts and
		// measurements. A layout operation modifies the positions and dimensions for nodes in the subtree.
		// The algorithm assumes that each node gets laid out a maximum of one time per tree layout, but
		// multiple measurements may be required to resolve all of the flex dimensions. We handle nodes with
		// measure functions specially here because they are the most expensive to measure, so it's worth
		// avoiding redundant measurements if at all possible.
		if ( node.HasMeasureFunc )
		{
			cachedResults = FindCachedMeasureFuncResult( node, ref layout, availableWidth, availableHeight, widthSizingMode, heightSizingMode, ownerWidth, ownerHeight );
		}
		else if ( performLayout )
		{
			ref var cachedLayout = ref layout.CachedLayout;
			if ( Num.InexactEquals( cachedLayout.AvailableWidth, availableWidth )
				&& Num.InexactEquals( cachedLayout.AvailableHeight, availableHeight )
				&& Num.InexactEquals( cachedLayout.OwnerWidth, ownerWidth )
				&& Num.InexactEquals( cachedLayout.OwnerHeight, ownerHeight )
				&& cachedLayout.WidthSizingMode == widthSizingMode
				&& cachedLayout.HeightSizingMode == heightSizingMode )
			{
				cachedResults = -2;
			}
		}
		else if ( layout.NextCachedMeasurementsIndex > 0 )
		{
			// Containers need an exact constraint and percentage-reference match. A partial entry only
			// serves requests for that dimension.
			for ( int i = 0; i < layout.NextCachedMeasurementsIndex; i++ )
			{
				ref var cachedMeasurement = ref layout.CachedMeasurements[i];
				if ( (cachedMeasurement.Scope == MeasureScope.Both || cachedMeasurement.Scope == scope)
					&& Num.InexactEquals( cachedMeasurement.AvailableWidth, availableWidth )
					&& Num.InexactEquals( cachedMeasurement.AvailableHeight, availableHeight )
					&& Num.InexactEquals( cachedMeasurement.OwnerWidth, ownerWidth )
					&& Num.InexactEquals( cachedMeasurement.OwnerHeight, ownerHeight )
					&& cachedMeasurement.WidthSizingMode == widthSizingMode
					&& cachedMeasurement.HeightSizingMode == heightSizingMode )
				{
					cachedResults = i;
					break;
				}
			}

			if ( cachedResults == -1 && scope != MeasureScope.Both )
			{
				cachedResults = TryReuseContainerMeasurement( node, ref layout, availableWidth, availableHeight, widthSizingMode, heightSizingMode, ownerWidth, ownerHeight, scope );
			}
		}

		if ( !needToVisitNode && cachedResults != -1 )
		{
			ref var cached = ref (cachedResults == -2 ? ref layout.CachedLayout : ref layout.CachedMeasurements[cachedResults]);
			layout.SetMeasuredDimension( Dimension.Width, cached.ComputedWidth );
			layout.SetMeasuredDimension( Dimension.Height, cached.ComputedHeight );
			layout.HadOverflow = cached.HadOverflow;

			// The ancestors being measured right now inherit this entry's taint just as if it were recomputed.
			if ( !cached.ContentBased )
			{
				s_nonContentMeasurements++;
			}
		}
		else
		{
			var nonContentBefore = s_nonContentMeasurements;
			var computedScope = CalculateLayoutImpl(
				node,
				availableWidth,
				availableHeight,
				ownerDirection,
				widthSizingMode,
				heightSizingMode,
				ownerWidth,
				ownerHeight,
				performLayout,
				depth,
				generationCount,
				scope
			);

			layout.LastOwnerDirection = ownerDirection;

			if ( cachedResults == -1 )
			{
				if ( layout.NextCachedMeasurementsIndex == LayoutResults.MaxCachedMeasurements )
				{
					layout.NextCachedMeasurementsIndex = 0;
				}

				ref var newCacheEntry = ref layout.CachedLayout;
				if ( !performLayout )
				{
					// Allocate a new measurement cache entry.
					newCacheEntry = ref layout.CachedMeasurements[layout.NextCachedMeasurementsIndex];
					layout.NextCachedMeasurementsIndex++;
				}

				newCacheEntry.AvailableWidth = availableWidth;
				newCacheEntry.AvailableHeight = availableHeight;
				newCacheEntry.OwnerWidth = ownerWidth;
				newCacheEntry.OwnerHeight = ownerHeight;
				newCacheEntry.WidthSizingMode = widthSizingMode;
				newCacheEntry.HeightSizingMode = heightSizingMode;
				newCacheEntry.ComputedWidth = layout.MeasuredDimension( Dimension.Width );
				newCacheEntry.ComputedHeight = layout.MeasuredDimension( Dimension.Height );
				newCacheEntry.Scope = computedScope;
				newCacheEntry.ContentBased = s_nonContentMeasurements == nonContentBefore;
				newCacheEntry.HadOverflow = layout.HadOverflow;
			}
		}

		if ( performLayout )
		{
			node.Layout.SetDimension( Dimension.Width, node.Layout.MeasuredDimension( Dimension.Width ) );
			node.Layout.SetDimension( Dimension.Height, node.Layout.MeasuredDimension( Dimension.Height ) );

			node.HasNewLayout = true;
			node.SetDirty( false );
		}

		layout.GenerationCount = generationCount;

		return needToVisitNode || cachedResults == -1;
	}

	/// <summary>
	/// Cache search for a node with a measure function: the layout cache first, then the measurement
	/// entries, each under the looser rules of <see cref="CanUseCachedMeasurement"/>. Returns -2 for the
	/// layout cache, an entry index, or -1. Such nodes are always measured on both axes, so an entry's scope
	/// needs no check.
	/// </summary>
	[MethodImpl( MethodImplOptions.NoInlining )]
	private static int FindCachedMeasureFuncResult(
		LayoutNode node,
		ref LayoutResults layout,
		float availableWidth,
		float availableHeight,
		SizingMode widthSizingMode,
		SizingMode heightSizingMode,
		float ownerWidth,
		float ownerHeight )
	{
		var marginAxisRow = node.Style.ComputeMarginForAxis( FlexDirection.Row, ownerWidth );
		var marginAxisColumn = node.Style.ComputeMarginForAxis( FlexDirection.Column, ownerWidth );

		// A measured leaf only depends on its containing block through its own percentages.
		// Containers retain the stricter owner checks because descendants can depend on that size too.
		// First, try to use the layout cache.
		ref var cachedLayout = ref layout.CachedLayout;
		if ( (!node.Style.UsesPercentages || OwnerSizesMatch( ref cachedLayout, ownerWidth, ownerHeight )) && CanUseCachedMeasurement(
			widthSizingMode,
			availableWidth,
			heightSizingMode,
			availableHeight,
			cachedLayout.WidthSizingMode,
			cachedLayout.AvailableWidth,
			cachedLayout.HeightSizingMode,
			cachedLayout.AvailableHeight,
			cachedLayout.ComputedWidth,
			cachedLayout.ComputedHeight,
			marginAxisRow,
			marginAxisColumn
		) )
		{
			return -2;
		}

		// Try to use the measurement cache.
		for ( int i = 0; i < layout.NextCachedMeasurementsIndex; i++ )
		{
			ref var cachedMeasurement = ref layout.CachedMeasurements[i];
			if ( (!node.Style.UsesPercentages || OwnerSizesMatch( ref cachedMeasurement, ownerWidth, ownerHeight )) && CanUseCachedMeasurement(
				widthSizingMode,
				availableWidth,
				heightSizingMode,
				availableHeight,
				cachedMeasurement.WidthSizingMode,
				cachedMeasurement.AvailableWidth,
				cachedMeasurement.HeightSizingMode,
				cachedMeasurement.AvailableHeight,
				cachedMeasurement.ComputedWidth,
				cachedMeasurement.ComputedHeight,
				marginAxisRow,
				marginAxisColumn
			) )
			{
				return i;
			}
		}

		return -1;
	}

	private static bool OwnerSizesMatch( ref CachedMeasurement cached, float ownerWidth, float ownerHeight )
	{
		return Num.InexactEquals( cached.OwnerWidth, ownerWidth ) && Num.InexactEquals( cached.OwnerHeight, ownerHeight );
	}

	/// <summary>
	/// A fit-content request on a container whose subtree cannot make a fit-content result
	/// depend on the available size can also be answered by a max-content or looser fit-content
	/// measurement whose result fits (see <see cref="CanReuseContainerMeasurement"/>) - this is what stops
	/// nested auto-sized containers from being re-measured once per ancestor. A request with no room would
	/// take the fixed-size shortcut in <see cref="CalculateLayoutImpl"/>, which is not content based, so it
	/// is never answered loosely. A loose match also records an entry for the request itself, so that a
	/// later request with the same constraints finds an exact match. Returns the entry to use, or -1. Only called for
	/// a single-dimension request whose exact search failed, on a node with cache entries.
	/// </summary>
	[MethodImpl( MethodImplOptions.NoInlining )]
	private static int TryReuseContainerMeasurement(
		LayoutNode node,
		ref LayoutResults layout,
		float availableWidth,
		float availableHeight,
		SizingMode widthSizingMode,
		SizingMode heightSizingMode,
		float ownerWidth,
		float ownerHeight,
		MeasureScope scope )
	{
		if ( node.SubtreeBlocksMeasureReuse
			|| (widthSizingMode != SizingMode.FitContent && heightSizingMode != SizingMode.FitContent) )
		{
			return -1;
		}

		var marginAxisRow = node.Style.ComputeMarginForAxis( FlexDirection.Row, ownerWidth );
		var marginAxisColumn = node.Style.ComputeMarginForAxis( FlexDirection.Column, ownerWidth );
		if ( (widthSizingMode == SizingMode.FitContent && availableWidth - marginAxisRow <= 0.0f)
			|| (heightSizingMode == SizingMode.FitContent && availableHeight - marginAxisColumn <= 0.0f) )
		{
			return -1;
		}

		for ( int i = 0; i < layout.NextCachedMeasurementsIndex; i++ )
		{
			ref var cachedMeasurement = ref layout.CachedMeasurements[i];
			if ( (cachedMeasurement.Scope != MeasureScope.Both && cachedMeasurement.Scope != scope)
				|| !OwnerSizesMatch( ref cachedMeasurement, ownerWidth, ownerHeight )
				|| !CanReuseContainerMeasurement(
					widthSizingMode,
					availableWidth,
					heightSizingMode,
					availableHeight,
					ref cachedMeasurement,
					marginAxisRow,
					marginAxisColumn,
					cachedMeasurement.ContentBased
				) )
			{
				continue;
			}

			if ( layout.NextCachedMeasurementsIndex == LayoutResults.MaxCachedMeasurements )
			{
				layout.NextCachedMeasurementsIndex = 0;
			}
			var slot = layout.NextCachedMeasurementsIndex++;
			ref var entry = ref layout.CachedMeasurements[slot];
			if ( slot != i )
			{
				entry.ComputedWidth = cachedMeasurement.ComputedWidth;
				entry.ComputedHeight = cachedMeasurement.ComputedHeight;
				entry.Scope = cachedMeasurement.Scope;
				entry.ContentBased = cachedMeasurement.ContentBased;
				entry.HadOverflow = cachedMeasurement.HadOverflow;
			}
			entry.AvailableWidth = availableWidth;
			entry.AvailableHeight = availableHeight;
			entry.OwnerWidth = ownerWidth;
			entry.OwnerHeight = ownerHeight;
			entry.WidthSizingMode = widthSizingMode;
			entry.HeightSizingMode = heightSizingMode;
			return slot;
		}

		return -1;
	}

	private static void InvalidateCaches( ref LayoutResults layout )
	{
		layout.NextCachedMeasurementsIndex = 0;
		layout.CachedLayout.AvailableWidth = -1;
		layout.CachedLayout.AvailableHeight = -1;
		layout.CachedLayout.OwnerWidth = -1;
		layout.CachedLayout.OwnerHeight = -1;
		layout.CachedLayout.WidthSizingMode = SizingMode.MaxContent;
		layout.CachedLayout.HeightSizingMode = SizingMode.MaxContent;
		layout.CachedLayout.ComputedWidth = -1;
		layout.CachedLayout.ComputedHeight = -1;
	}

	private static bool SizeIsExactAndMatchesOldMeasuredSize( SizingMode sizeMode, float size, float lastSize, float lastComputedSize )
	{
		// Checking only that the exact size equals the old measured size is sound when the measure
		// function saw that size, not when a min size clamped the result above the space the content was
		// wrapped into (the entry's height then belongs to a narrower width). An entry computed with less
		// room than it reports only serves the same spec.
		return sizeMode == SizingMode.StretchFit && Num.InexactEquals( size, lastComputedSize )
			&& (Num.IsUndefined( lastSize ) || lastComputedSize <= lastSize || Num.InexactEquals( lastComputedSize, lastSize ));
	}

	private static bool OldSizeIsMaxContentAndStillFits( SizingMode sizeMode, float size, SizingMode lastSizeMode, float lastComputedSize )
	{
		return sizeMode == SizingMode.FitContent
			&& lastSizeMode == SizingMode.MaxContent
			&& (size >= lastComputedSize || Num.InexactEquals( size, lastComputedSize ));
	}

	private static bool NewSizeIsStricterAndStillValid(
		SizingMode sizeMode,
		float size,
		SizingMode lastSizeMode,
		float lastSize,
		float lastComputedSize )
	{
		return lastSizeMode == SizingMode.FitContent
			&& sizeMode == SizingMode.FitContent
			&& Num.IsDefined( lastSize )
			&& Num.IsDefined( size )
			&& Num.IsDefined( lastComputedSize )
			&& lastSize > size
			&& (lastComputedSize <= size || Num.InexactEquals( size, lastComputedSize ));
	}

	internal static bool CanUseCachedMeasurement(
		SizingMode widthMode, float availableWidth, SizingMode heightMode, float availableHeight,
		SizingMode lastWidthMode, float lastAvailableWidth, SizingMode lastHeightMode, float lastAvailableHeight,
		float lastComputedWidth, float lastComputedHeight, float marginRow, float marginColumn )
	{
		if ( (Num.IsDefined( lastComputedHeight ) && lastComputedHeight < 0) || (Num.IsDefined( lastComputedWidth ) && lastComputedWidth < 0) )
		{
			return false;
		}

		var hasSameWidthSpec = lastWidthMode == widthMode && Num.InexactEquals( lastAvailableWidth, availableWidth );
		var hasSameHeightSpec = lastHeightMode == heightMode && Num.InexactEquals( lastAvailableHeight, availableHeight );

		// The three looser rules compare sizes the measure function saw, i.e. without the node's margins.
		// Passing the cached available size with margins to NewSizeIsStricterAndStillValid while
		// subtracting them from the new one, so a request with more room than the cached one can look
		// stricter and be answered with text wrapped at the narrower width. Both sides are on the same
		// footing here (the margins are the node's own, so they are the same for both requests), and the
		// exact-size rule also gets the cached available size (see SizeIsExactAndMatchesOldMeasuredSize).

		var widthIsCompatible = hasSameWidthSpec
			|| SizeIsExactAndMatchesOldMeasuredSize( widthMode, availableWidth - marginRow, lastAvailableWidth - marginRow, lastComputedWidth )
			|| OldSizeIsMaxContentAndStillFits( widthMode, availableWidth - marginRow, lastWidthMode, lastComputedWidth )
			|| NewSizeIsStricterAndStillValid( widthMode, availableWidth - marginRow, lastWidthMode, lastAvailableWidth - marginRow, lastComputedWidth );

		var heightIsCompatible = hasSameHeightSpec
			|| SizeIsExactAndMatchesOldMeasuredSize( heightMode, availableHeight - marginColumn, lastAvailableHeight - marginColumn, lastComputedHeight )
			|| OldSizeIsMaxContentAndStillFits( heightMode, availableHeight - marginColumn, lastHeightMode, lastComputedHeight )
			|| NewSizeIsStricterAndStillValid( heightMode, availableHeight - marginColumn, lastHeightMode, lastAvailableHeight - marginColumn, lastComputedHeight );

		return widthIsCompatible && heightIsCompatible;
	}

	/// <summary>
	/// Two compatibility rules used for measure functions are exact for a flex container as well, provided nothing
	/// in its subtree makes a fit-content result depend on the available size once the content fits
	/// (<see cref="LayoutNode.SubtreeBlocksMeasureReuse"/>): with no max size to clamp against, the flex algorithm
	/// distributes no free space under fit-content or max-content, so the children keep their flex bases,
	/// and those bases are themselves fit-content measurements of children whose content fits, so by
	/// induction (down to the measure functions, where the same rules apply) the whole
	/// subtree resolves identically:
	/// <list type="bullet">
	/// <item>fit-content with at least the cached max-content size available yields the max-content size;</item>
	/// <item>a tighter fit-content that still holds an earlier fit-content result yields that result.</item>
	/// </list>
	/// The one place the flex algorithm sizes from the available space instead of the content is
	/// <see cref="MeasureNodeWithFixedSize"/> when a fit-content request has no room, so an entry whose
	/// computation went through that anywhere in the subtree (<paramref name="contentBased"/> false) only
	/// serves an exact match. This keeps the available and computed sizes on the same footing (both without
	/// margins) and only looks at an entry that holds the dimension in question.
	/// </summary>
	internal static bool CanReuseContainerMeasurement(
		SizingMode widthMode,
		float availableWidth,
		SizingMode heightMode,
		float availableHeight,
		ref CachedMeasurement cached,
		float marginRow,
		float marginColumn,
		bool contentBased )
	{
		return ContainerAxisIsCompatible(
			widthMode,
			availableWidth,
			cached.WidthSizingMode,
			cached.AvailableWidth,
			cached.ComputedWidth,
			marginRow,
			contentBased
		) && ContainerAxisIsCompatible(
			heightMode,
			availableHeight,
			cached.HeightSizingMode,
			cached.AvailableHeight,
			cached.ComputedHeight,
			marginColumn,
			contentBased
		);
	}

	private static bool ContainerAxisIsCompatible(
		SizingMode mode,
		float available,
		SizingMode lastMode,
		float lastAvailable,
		float lastComputed,
		float margin,
		bool contentBased )
	{
		if ( lastMode == mode && Num.InexactEquals( lastAvailable, available ) )
		{
			return true;
		}

		if ( !contentBased || mode != SizingMode.FitContent || Num.IsUndefined( lastComputed ) )
		{
			return false;
		}

		var size = available - margin;
		if ( lastMode == SizingMode.MaxContent )
		{
			return size >= lastComputed || Num.InexactEquals( size, lastComputed );
		}

		if ( lastMode == SizingMode.FitContent )
		{
			var lastSize = lastAvailable - margin;
			return lastSize > size && (lastComputed <= size || Num.InexactEquals( size, lastComputed ));
		}

		return false;
	}
}
