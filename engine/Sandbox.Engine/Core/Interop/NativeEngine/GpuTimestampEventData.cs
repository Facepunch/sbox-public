using System.Runtime.InteropServices;

namespace NativeEngine;

/// <summary>
/// Matches SceneSystemTimestampEventData_t. The event name is copied through the string binding.
/// </summary>
[StructLayout( LayoutKind.Sequential )]
internal struct GpuTimestampEventData
{
	public int Kind;
	public ulong SubmissionId;
	public ulong EndSubmissionId;
	public ulong WaitForSubmissionId;
	public ulong WaitStageMask;
	public double DeviationMs;
	public int CommandBuffers;
	[MarshalAs( UnmanagedType.I1 )]
	public bool WaitForAcquire;
	public double StartMs;
	public double EndMs;
	public int Parent;
	public int Queue;
}
