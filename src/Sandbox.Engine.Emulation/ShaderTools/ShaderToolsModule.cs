using System;
using System.Runtime.InteropServices;
using System.IO;
using System.Text;

namespace Sandbox.Engine.Emulation.ShaderTools;

/// <summary>
/// Minimal ShaderTools surface to satisfy ShaderCompile hot-path.
/// Currently returns the input source unchanged.
/// </summary>
public static unsafe class ShaderToolsModule
{
    public static void Init( void** native )
    {
        // Indices from Interop.Engine.cs (nativeFunctions[2550..2551])
        native[2550] = (void*)(delegate* unmanaged<IntPtr, IntPtr>)&ShaderTools_GetShaderSource;
        native[2551] = (void*)(delegate* unmanaged<IntPtr, long, int, IntPtr>)&ShaderTools_MaskShaderSource;
    }

    [UnmanagedCallersOnly]
    private static IntPtr ShaderTools_GetShaderSource( IntPtr pShaderFile )
    {
        try
        {
            var path = pShaderFile == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8( pShaderFile ) ?? string.Empty;
            if ( string.IsNullOrEmpty( path ) || !File.Exists( path ) )
                return Marshal.StringToHGlobalAnsi( string.Empty );

            var text = File.ReadAllText( path, Encoding.UTF8 );
            return Marshal.StringToHGlobalAnsi( text ?? string.Empty );
        }
        catch
        {
            return Marshal.StringToHGlobalAnsi( string.Empty );
        }
    }

    [UnmanagedCallersOnly]
    private static IntPtr ShaderTools_MaskShaderSource( IntPtr sourcecode, long program, int bIsForCrc )
    {
        var src = sourcecode == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8( sourcecode ) ?? string.Empty;
        // Prepend system.fxc include to align with managed expectations
        var builder = new StringBuilder();
        builder.AppendLine( "#include \"system.fxc\"" );
        builder.Append( src );
        return Marshal.StringToHGlobalAnsi( builder.ToString() );
    }
}
