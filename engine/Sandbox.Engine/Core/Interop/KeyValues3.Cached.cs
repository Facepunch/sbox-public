/// <summary>Hand-written additions to the generated <c>KeyValues3</c> binding.</summary>
internal unsafe readonly partial struct KeyValues3
{
	/// <summary>
	/// As <c>GetMemberString</c>, through <see cref="Sandbox.Interop.GetCachedString"/>: for members read again and again with
	/// the same few values (animation events' attachments), so a repeat allocates nothing.
	/// </summary>
	internal string GetMemberStringCached( string key )
	{
		if ( self == IntPtr.Zero ) Sandbox.Interop.ThrowNullSelf( "KeyValues3", "GetMemberString" );
		var name = new Sandbox.Interop.InteropString( key, stackalloc byte[256] );
		try
		{
			return Sandbox.Interop.GetCachedString( __N.KeyValues3_GetMemberString( self, name.Pointer ) );
		}
		finally
		{
			name.Free();
		}
	}
}
