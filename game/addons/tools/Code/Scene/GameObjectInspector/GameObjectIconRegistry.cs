using System.Runtime.CompilerServices;
using Sandbox;

namespace Editor
{
    /// <summary>
    /// Registry to store editor-only icons for GameObjects.
    /// Uses ConditionalWeakTable so entries do not prevent GC of GameObjects.
    /// </summary>
    static class GameObjectIconRegistry
    {
        static readonly ConditionalWeakTable<GameObject, IconHolder> _icons = new();

        class IconHolder
        {
            public string Icon;
        }

        public static string GetIcon( GameObject go )
        {
            if ( go is null ) return null;
            if ( _icons.TryGetValue( go, out var h ) ) return h.Icon;
            return null;
        }

        public static void SetIcon( GameObject go, string icon )
        {
            if ( go is null ) return;
            if ( string.IsNullOrEmpty( icon ) )
            {
                _icons.Remove( go );
                return;
            }

            var holder = _icons.GetOrCreateValue( go );
            holder.Icon = icon;
        }

        public static void ClearIcon( GameObject go )
        {
            if ( go is null ) return;
            _icons.Remove( go );
        }
    }
}
