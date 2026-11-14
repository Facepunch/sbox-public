using Borealis.Gpu;

namespace Borealis.Backend;

/// <summary>
/// A draw census (<see cref="BackendDevice.Census"/>, debug only): while one runs, each model draw and GPU-driven multi-draw
/// this context records is counted with its pass (the innermost GPU scope), what it bound and what it drew, and whatever a
/// list records between scopes goes on the scope it's in.
/// </summary>
internal sealed partial class BackendContext
{
	readonly List<string> censusScopes = new();

	/// <summary>The GPU-driven multi-draw being recorded is forward's compacted one (its triangles that own a sample).</summary>
	bool censusCompacted;

	string CensusPass => censusScopes.Count == 0 ? "(outside a layer)" : censusScopes[^1];

	void CensusBegin()
	{
		Census = device.Census is not null;
		CensusNote = null;
		censusScopes.Clear();
	}

	/// <summary>Put what the list recorded since its last mark on the current scope, then open <paramref name="name"/> or close the current one.</summary>
	void CensusScope( string name, bool pop )
	{
		if ( device.Census is not { } census || recording is null ) return;
		census.Mark( recording.List, CensusPass );
		if ( pop )
		{
			if ( censusScopes.Count > 0 ) censusScopes.RemoveAt( censusScopes.Count - 1 );
		}
		else if ( name is not null )
		{
			censusScopes.Add( name );
		}

		CensusNote = null;
	}

	/// <summary>A model draw, counted: what it bound and drew, its variant and what the feature drawing it said it is.</summary>
	bool DrawCounted( GpuMaterial material, StringToken shaderMode, RenderMesh mesh, in RenderMesh.Draw draw, int firstInstance, int instances, int depthBias, float slopeScaledDepthBias, bool stripPixelShader, bool skinned, in IndirectDraws indirect )
	{
		var census = device.Census;
		var list = recording?.List;
		var before = list is null ? default : PassTimer.Recorded.Of( list );
		var drawn = DrawTimed( material, shaderMode, mesh, draw, firstInstance, instances, depthBias, slopeScaledDepthBias, stripPixelShader, skinned, indirect );
		if ( !drawn || census is null || list is null ) return drawn;

		var recorded = PassTimer.Recorded.Of( list ).Since( before );
		var counted = new DrawCensus.Draw
		{
			Pass = CensusPass,
			Path = DrawCensus.Path.Classic,
			Kind = CensusNote ?? "(not said)",
			Material = material?.Name,
			Calls = recorded.Draws,
			Pipelines = recorded.Pipelines,
			Sets = recorded.Sets,
			Instances = instances,
			MaxCommands = indirect.Count == 0 ? 1 : indirect.Count,
		};

		if ( material is SurfaceMaterial surface )
		{
			counted.Shader = $"{DrawCensus.ShaderName( surface.Shader )} (surface)";
			counted.Mode = "surface";
		}
		else if ( material is BackendMaterial m && ReferenceEquals( lastKey.Material, m ) && lastKey.Variant is { } variant )
		{
			DrawCensus.Describe( counted, variant, lastKey.DepthOnly );
			counted.Layout = DrawCensus.LayoutName( lastKey.Layout );
		}
		else
		{
			counted.Shader = DrawCensus.ShaderName( (material as BackendMaterial)?.Shader?.Name );
		}

		if ( indirect.Count == 0 )
		{
			counted.Commands = 1;
			counted.Triangles = draw.IndexCount / 3 * (long)instances;
		}
		else
		{
			counted.Arguments = indirect.Arguments;
			counted.ArgumentsOffset = indirect.Offset;
			if ( indirect.CountBuffer.IsValid ) (counted.CountBuffer, counted.CountOffset) = (indirect.CountBuffer, indirect.CountOffset);
		}

		census.Add( counted );
		return drawn;
	}

	/// <summary>Before a GPU-driven call's buckets draw: each one's census number (given when it first draws), shader and materials.</summary>
	void CensusBuckets( ReadOnlySpan<GpuDrivenBatch> batches )
	{
		var materials = new HashSet<GpuMaterial>( ReferenceEqualityComparer.Instance );
		for ( int b = 0; b < gpuDrivenBucketCount; b++ )
		{
			var bucket = gpuDrivenBuckets[b];
			materials.Clear();
			foreach ( var (_, index) in bucket.Batches ) materials.Add( batches[index].Material );
			bucket.CensusId = -1;
			bucket.CensusMaterials = materials.Count;
			bucket.CensusShader = bucket.Variant is { } variant ? DrawCensus.ShaderName( variant.Shader?.Name )
				: bucket.Batches.Count > 0 && batches[bucket.Batches[0].Batch].Material is SurfaceMaterial surface ? $"{DrawCensus.ShaderName( surface.Shader )} (surface)" : "(surface)";
		}
	}

	/// <summary>A bucket's multi-draw, counted with what it bound since <paramref name="before"/> and where its commands are.</summary>
	void CensusMultiDraw( GpuDrivenBucket bucket, DrawCensus.Path path, PassTimer.Recorded before, BufferHandle arguments, long offset, int count, long countOffset )
	{
		if ( device.Census is not { } census || recording is null ) return;
		if ( bucket.CensusId < 0 ) bucket.CensusId = census.NextBucket();

		var recorded = PassTimer.Recorded.Of( recording.List ).Since( before );
		var counted = new DrawCensus.Draw
		{
			Pass = CensusPass,
			Path = path,
			Kind = path == DrawCensus.Path.BucketIds ? (bucket.IdsShader ? "ids (borealis_gpu_driven_ids)" : "ids (own pixel shader)") : censusCompacted ? "compacted triangles" : "whole fragments",
			Bucket = bucket.CensusId,
			BucketMaterials = bucket.CensusMaterials,
			BucketBatches = bucket.Batches.Count,
			BucketKey = $"attributes {bucket.AttributesKey:x}{(bucket.DepthOnly ? " depth-only" : "")}{(bucket.NoColor ? " no-colour" : "")}{(bucket.PixelMaterial != 0 ? $" material {bucket.PixelMaterial}" : "")}",
			Material = bucket.Material?.Name,
			Layout = "pulled",
			Calls = recorded.Draws,
			Pipelines = recorded.Pipelines,
			Sets = recorded.Sets,
			MaxCommands = count,
			Arguments = arguments,
			ArgumentsOffset = offset,
			CountBuffer = countOffset >= 0 ? arguments : default,
			CountOffset = countOffset,
		};

		if ( bucket.Variant is { } variant )
		{
			DrawCensus.Describe( counted, variant, bucket.DepthOnly && path != DrawCensus.Path.BucketIds );
		}
		else
		{
			counted.Mode = "surface";
			counted.State = $"{(bucket.SurfaceState.TwoSided ? "two-sided" : "cull back")} {(bucket.Surface?.Pass.Pixel is null ? "no-ps" : "ps")}";
		}

		counted.Shader = bucket.CensusShader ?? counted.Shader;
		census.Add( counted );
	}
}
