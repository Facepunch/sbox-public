using static Sandbox.IByteParsable;

namespace Sandbox.Navigation.Generation;

internal partial class CompactHeightfield : IByteParsable<CompactHeightfield>
{
	public static CompactHeightfield Read( ref ByteStream stream, ByteParseOptions o = default )
	{
		var compactHeightfield = GetPooled();
		var width = stream.Read<int>();
		var height = stream.Read<int>();
		var spanCount = stream.Read<int>();
		var walkableHeight = stream.Read<int>();
		var walkableClimb = stream.Read<int>();

		var bMin = stream.Read<Vector3>();
		var bMax = stream.Read<Vector3>();

		var cellSize = stream.Read<float>();
		var cellHeight = stream.Read<float>();

		compactHeightfield.Init( width, height, spanCount, walkableHeight, walkableClimb, bMin, bMax, cellSize, cellHeight );

		var cells = compactHeightfield.Cells;
		ReadCells( ref stream, cells );

		var spans = compactHeightfield.Spans;
		ReadSpans( ref stream, spans, cells );

		var areas = compactHeightfield.Areas;
		ReadAreas( ref stream, areas );

		return compactHeightfield;
	}

	public static object ReadObject( ref ByteStream stream, ByteParseOptions o = default )
	{
		return Read( ref stream, o );
	}

	public static void Write( ref ByteStream stream, CompactHeightfield value, ByteParseOptions o = default )
	{
		stream.Write( value.Width );
		stream.Write( value.Height );
		stream.Write( value.SpanCount );
		stream.Write( value.WalkableHeight );
		stream.Write( value.WalkableClimb );
		stream.Write( value.BMin );
		stream.Write( value.BMax );
		stream.Write( value.CellSize );
		stream.Write( value.CellHeight );

		WriteCells( ref stream, value.Cells );
		WriteSpans( ref stream, value.Spans, value.Cells );
		WriteAreas( ref stream, value.Areas );
	}

	public static void WriteObject( ref ByteStream stream, object value, ByteParseOptions o = default )
	{
		Write( ref stream, value as CompactHeightfield, o );
	}

	private static void ReadCells( ref ByteStream stream, Span<CompactCell> cells )
	{
		var spanCursor = 0;
		for ( var i = 0; i < cells.Length; i++ )
		{
			var count = stream.Read<int>();
			ref var cell = ref cells[i];
			cell.Index = spanCursor;
			cell.Count = count;
			spanCursor += count;
		}
	}

	private static void WriteCells( ref ByteStream stream, ReadOnlySpan<CompactCell> cells )
	{
		for ( var index = 0; index < cells.Length; index++ )
		{
			stream.Write( cells[index].Count );
		}
	}

	private static void ReadSpans( ref ByteStream stream, Span<CompactSpan> spans, ReadOnlySpan<CompactCell> cells )
	{
		var spanIndex = 0;
		for ( var i = 0; i < cells.Length; i++ )
		{
			ref readonly var cell = ref cells[i];
			for ( var j = 0; j < cell.Count; j++ )
			{
				ref var span = ref spans[spanIndex++];
				span.StartY = stream.Read<ushort>();
				span.Region = stream.Read<ushort>();

				var packed = stream.Read<int>();
				span.Con = packed & 0xFFFFFF;
				span.Height = (byte)(packed >> 24);
			}
		}
	}

	private static void WriteSpans( ref ByteStream stream, ReadOnlySpan<CompactSpan> spans, ReadOnlySpan<CompactCell> cells )
	{
		var spanIndex = 0;
		for ( var i = 0; i < cells.Length; i++ )
		{
			ref readonly var cell = ref cells[i];
			for ( var j = 0; j < cell.Count; j++ )
			{
				ref readonly var span = ref spans[spanIndex++];
				stream.Write( span.StartY );
				stream.Write( span.Region );
				var packed = (span.Con & 0xFFFFFF) | ((span.Height & 0xFF) << 24);
				stream.Write( packed );
			}
		}
	}

	private static void ReadAreas( ref ByteStream stream, Span<int> areas )
	{
		var index = 0;
		while ( index < areas.Length )
		{
			var runLength = stream.Read<int>();
			var areaValue = stream.Read<int>();

			for ( var i = 0; i < runLength && index < areas.Length; i++ )
			{
				areas[index++] = areaValue;
			}
		}
	}

	private static void WriteAreas( ref ByteStream stream, ReadOnlySpan<int> areas )
	{
		var index = 0;
		while ( index < areas.Length )
		{
			var areaValue = areas[index];
			var runLength = 1;
			while ( index + runLength < areas.Length && areas[index + runLength] == areaValue )
			{
				runLength++;
			}

			stream.Write( runLength );
			stream.Write( areaValue );
			index += runLength;
		}
	}

}
