using BenchmarkDotNet.Attributes;
using System.Buffers;
using System.Runtime.InteropServices;

[MemoryDiagnoser]
public unsafe class MemoryAlloc
{
	const int allocSize = 1024 * 32;

	[Benchmark]
	public void AllocHGlobal()
	{
		var ptr = Marshal.AllocHGlobal( allocSize );
		Marshal.FreeHGlobal( ptr );
	}

	[Benchmark]
	public void MemoryPool()
	{
		using var ptr = MemoryPool<byte>.Shared.Rent( allocSize );
		using var ptrPinned = ptr.Memory.Pin();
	}

	[Benchmark]
	public void ArrayPool()
	{
		var ptr = ArrayPool<byte>.Shared.Rent( allocSize );
		ArrayPool<byte>.Shared.Return( ptr );
	}

	[Benchmark]
	public void ArrayPoolPinned()
	{
		var ptr = ArrayPool<byte>.Shared.Rent( allocSize );
		var ptrHandle = GCHandle.Alloc( ptr, GCHandleType.Pinned );
		ptrHandle.Free();
		ArrayPool<byte>.Shared.Return( ptr );
	}

	[Benchmark]
	public void NativeMemoryAlloc()
	{
		var data = NativeMemory.Alloc( allocSize );
		NativeMemory.Free( data );
	}
}
