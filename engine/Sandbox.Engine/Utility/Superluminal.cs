using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sandbox.Utility;

class Superluminal : IDisposable
{
	IntPtr _text;
	uint _color;

	public Superluminal( string name, Color color )
	{
		_text = Marshal.StringToCoTaskMemUTF8( name );

		Color32 c32 = color;
		_color = ((((uint)(c32.r)) << 24) | (((uint)(c32.g)) << 16) | (((uint)(c32.b)) << 8) | (uint)0xFF);
	}
	~Superluminal()
	{
		Marshal.FreeCoTaskMem( _text );
		_text = default;
	}
	public IDisposable Start( string extraData = null,
		[CallerFilePath] string file = null,
		[CallerLineNumber] int line = 0,
		[CallerMemberName] string member = null )
	{
		ProfilerZones.Begin( _text, _color, extraData, file, line, member );
		return this;
	}

	public void Dispose()
	{
		ProfilerZones.End();
	}
}
