using System.Runtime.InteropServices;

namespace Borealis.Features;

/// <summary>
/// Opaque custom objects drawn after every other opaque draw. Native draws the world's opaque layer before the dynamic
/// one (<c>Static Opaque Forward</c>, then <c>Dynamic Opaque Forward</c>), and custom objects are dynamic: one that blends
/// in an opaque layer without writing depth (a sprite batch drawn opaque and additive) stays on top of the meshes behind
/// it, where drawn before them it would be painted over by those that passed the prepass's depth.
/// </summary>
internal sealed partial class MeshRenderFeature
{
	/// <summary>
	/// Move the opaque list's custom objects into <see cref="RunLists.LateCustom"/>, keeping their order.
	/// </summary>
	static void TakeCustom( RunLists layers )
	{
		var runs = CollectionsMarshal.AsSpan( layers.Opaque );
		var items = layers.ItemsByBit[0];
		var kept = 0;
		for ( int i = 0; i < items.Count; i++ )
		{
			var item = items[i];
			if ( runs[item.Run].Custom is not null )
			{
				layers.LateCustom.Add( item );
				continue;
			}

			items[kept++] = item;
		}

		CollectionsMarshal.SetCount( items, kept );
	}

	/// <summary>The view's late custom draws <paramref name="layer"/> draws: those of layers drawing the opaque list.</summary>
	internal int LateCustomCount( ViewPass view, MeshLayer layer ) => (layer.Runs & MeshRuns.Opaque) != 0 ? view.State<RunLists>( this ).LateCustom.Count : 0;

	/// <summary>Draw a range of the view's late custom draws.</summary>
	internal void DrawLateCustom( GpuContext context, ViewPass view, MeshLayer layer, int first, int count, ref RenderStats stats )
	{
		var lists = view.State<RunLists>( this );
		var items = CollectionsMarshal.AsSpan( lists.LateCustom );
		DrawItems( context, view, CollectionsMarshal.AsSpan( lists.Opaque ), items.Slice( first, Math.Min( count, items.Length - first ) ), layer, ref stats, MeshRuns.Opaque );
	}
}

/// <summary>
/// Draws the mesh feature's late custom objects (<see cref="MeshRenderFeature.RunLists.LateCustom"/>), after the features before it. Owns no
/// objects; custom objects record on the main thread.
/// </summary>
internal sealed class LateCustomFeature( MeshRenderFeature meshes ) : RenderFeature
{
	public override Type ObjectType => typeof( CustomObject );

	internal override bool Accepts( Type type ) => false;

	internal override int RunCount( ViewPass view, MeshLayer layer ) => meshes.LateCustomCount( view, layer );

	internal override bool CanRecordOffMainThread( ViewPass view, MeshLayer layer ) => !layer.DrawsCustomObjectsIn( view ) || meshes.LateCustomCount( view, layer ) == 0;

	internal override void Draw( GpuContext context, ViewPass view, MeshLayer layer, int first, int count, ref RenderStats stats ) => meshes.DrawLateCustom( context, view, layer, first, count, ref stats );
}
