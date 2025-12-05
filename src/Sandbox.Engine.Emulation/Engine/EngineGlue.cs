using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Sandbox;
using Sandbox.Internal;
using Sandbox.Engine.Emulation.Common;
using Sandbox.Engine.Emulation.Model;
using Sandbox.Engine.Emulation.Texture;
using Sandbox.Engine.Emulation.Steam;

namespace Sandbox.Engine.Emulation.Engine;

/// <summary>
/// Module d'émulation pour les fonctions EngineGlue (EngineGlue_*).
/// Ces fonctions fournissent des utilitaires pour le moteur (KeyValues, StringToken, SearchPath, etc.).
/// </summary>
public static unsafe class EngineGlue
{
    // Cache pour les chaînes allouées (GetStringTokenValue)
    private static readonly List<IntPtr> _allocatedStrings = new();
    private static readonly Dictionary<uint, IntPtr> _tokenStringPtrs = new();
    
    // État du logging
    private static bool _verboseLogging = false;
    
    // Cache pour les chemins de recherche
    private static readonly HashSet<string> _searchPaths = new();
    
    // Helper pour lire JSON depuis un buffer (évite d'appeler directement [UnmanagedCallersOnly])
    private static IntPtr ReadCompiledResourceFileJsonHelper(byte[] data)
    {
        if (data == null || data.Length == 0) return IntPtr.Zero;
        
        try
        {
            // Lire les premiers bytes pour détecter le format
            byte firstByte = data[0];
            
            // Si ça commence par '{' ou '[', c'est probablement du JSON
            if (firstByte == '{' || firstByte == '[')
            {
                // Lire comme chaîne UTF-8
                string json = Encoding.UTF8.GetString(data);
                if (!string.IsNullOrEmpty(json))
                {
                    // Allouer et retourner une copie
                    byte[] utf8Bytes = Encoding.UTF8.GetBytes(json);
                    IntPtr ptr = Marshal.AllocHGlobal(utf8Bytes.Length + 1);
                    Marshal.Copy(utf8Bytes, 0, ptr, utf8Bytes.Length);
                    Marshal.WriteByte(ptr, utf8Bytes.Length, 0);
                    
                    lock (_allocatedStrings)
                    {
                        _allocatedStrings.Add(ptr);
                    }
                    
                    return ptr;
                }
            }
            
            return IntPtr.Zero;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }
    
    /// <summary>
    /// Initialise le module EngineGlue en patchant les fonctions natives.
    /// </summary>
    public static void Init(void** native)
    {
        // Index calculés depuis Interop.Engine.cs (lignes 16070-16085)
        native[1323] = (void*)(delegate* unmanaged<IntPtr, IntPtr>)&EngineGlue_JsonToKeyValues3;
        native[1324] = (void*)(delegate* unmanaged<IntPtr, IntPtr>)&EngineGlue_KeyValuesToJson;
        native[1325] = (void*)(delegate* unmanaged<IntPtr, IntPtr>)&EngineGlue_KeyValues3ToJson;
        native[1326] = (void*)(delegate* unmanaged<IntPtr, IntPtr>)&EngineGlue_LoadKeyValues3;
        native[1327] = (void*)(delegate* unmanaged<IntPtr, uint>)&EngineGlue_GetStringToken;
        native[1328] = (void*)(delegate* unmanaged<uint, IntPtr>)&EngineGlue_GetStringTokenValue;
        native[1329] = (void*)(delegate* unmanaged<IntPtr, IntPtr, int, void>)&EngineGlue_AddSearchPath;
        native[1330] = (void*)(delegate* unmanaged<IntPtr, IntPtr, int>)&EngineGlue_RemoveSearchPath;
        native[1331] = (void*)(delegate* unmanaged<ulong>)&EngineGlue_ApproximateProcessMemoryUsage;
        native[1332] = (void*)(delegate* unmanaged<IntPtr, IntPtr>)&EngineGlue_ReadCompiledResourceFileJson;
        native[1333] = (void*)(delegate* unmanaged<IntPtr, IntPtr, int*, IntPtr>)&EngineGlue_ReadCompiledResourceFileBlock;
        native[1334] = (void*)(delegate* unmanaged<IntPtr, IntPtr>)&EngineGlue_ReadCompiledResourceFileJsonFromFilesystem;
        native[1335] = (void*)(delegate* unmanaged<int, void>)&EngineGlue_SetEngineLoggingVerbose;
        native[1336] = (void*)(delegate* unmanaged<void>)&EngineGlue_RequestWebAuthTicket;
        native[1337] = (void*)(delegate* unmanaged<void>)&EngineGlue_CancelWebAuthTicket;
        native[1338] = (void*)(delegate* unmanaged<IntPtr>)&EngineGlue_GetWebAuthTicket;
        
        // Glue_Networking functions (indices depuis Interop.Engine.cs lignes 16588-16611)
        native[1723] = (void*)(delegate* unmanaged<void>)&Glue_Networking_RunCallbacks;
        native[1724] = (void*)(delegate* unmanaged<int, IntPtr, void>)&Glue_Networking_SetDebugFunction;
        native[1725] = (void*)(delegate* unmanaged<IntPtr, long>)&Glue_Networking_GetAuthenticationStatus;
        native[1726] = (void*)(delegate* unmanaged<IntPtr, long>)&Glue_Networking_GetRelayNetworkStatus;
        native[1727] = (void*)(delegate* unmanaged<int, IntPtr>)&Glue_Networking_CreateSocket;
        native[1728] = (void*)(delegate* unmanaged<int, IntPtr>)&Glue_Networking_CreateIpBasedSocket;
        native[1729] = (void*)(delegate* unmanaged<IntPtr, void>)&Glue_Networking_CloseSocket;
        native[1730] = (void*)(delegate* unmanaged<IntPtr, IntPtr>)&Glue_Networking_GetSocketAddress;
        native[1731] = (void*)(delegate* unmanaged<void>)&Glue_Networking_BeginAsyncRequestFakeIP;
        native[1732] = (void*)(delegate* unmanaged<IntPtr>)&Glue_Networking_GetIdentity;
        native[1733] = (void*)(delegate* unmanaged<IntPtr>)&Glue_Networking_CreatePollGroup;
        native[1734] = (void*)(delegate* unmanaged<IntPtr, void>)&Glue_Networking_DestroyPollGroup;
        native[1735] = (void*)(delegate* unmanaged<IntPtr, IntPtr, void>)&Glue_Networking_SetPollGroup;
        native[1736] = (void*)(delegate* unmanaged<IntPtr, IntPtr, int, int>)&Glue_Networking_GetPollGroupMessages;
        native[1737] = (void*)(delegate* unmanaged<ulong, int, IntPtr>)&Glue_Networking_ConnectToSteamId;
        native[1738] = (void*)(delegate* unmanaged<IntPtr, IntPtr>)&Glue_Networking_ConnectToIpAddress;
        native[1739] = (void*)(delegate* unmanaged<IntPtr, int, IntPtr, void>)&Glue_Networking_CloseConnection;
        native[1740] = (void*)(delegate* unmanaged<IntPtr, void>)&Glue_Networking_AcceptConnection;
        native[1741] = (void*)(delegate* unmanaged<IntPtr, void>)&Glue_Networking_FlushMessagesOnConnection;
        native[1742] = (void*)(delegate* unmanaged<IntPtr, IntPtr, int, int, long>)&Glue_Networking_SendMessage;
        native[1743] = (void*)(delegate* unmanaged<IntPtr, IntPtr, int, int>)&Glue_Networking_GetConnectionMessages;
        native[1744] = (void*)(delegate* unmanaged<IntPtr, int>)&Glue_Networking_GetConnectionState;
        native[1745] = (void*)(delegate* unmanaged<IntPtr, IntPtr>)&Glue_Networking_GetConnectionDescription;
        native[1746] = (void*)(delegate* unmanaged<IntPtr, ulong>)&Glue_Networking_GetConnectionSteamId;
        
        // Glue_Resources functions (indices depuis Interop.Engine.cs - à vérifier)
        native[1751] = (void*)(delegate* unmanaged<IntPtr, IntPtr>)&Glue_Resources_GetMaterial;
        native[1752] = (void*)(delegate* unmanaged<IntPtr, IntPtr>)&Glue_Resources_GetTexture;
        native[1753] = (void*)(delegate* unmanaged<IntPtr, IntPtr>)&Glue_Resources_GetModel;
        native[1754] = (void*)(delegate* unmanaged<IntPtr, IntPtr>)&Glue_Resources_GetAnimationGraph;
        native[1755] = (void*)(delegate* unmanaged<IntPtr, IntPtr>)&Glue_Resources_GetShader;
        
        Console.WriteLine("[NativeAOT] EngineGlue module initialized");
    }
    
    // Structure simple pour représenter KeyValues3 (émulation)
    private class SimpleKeyValues3
    {
        public Dictionary<string, object> Data { get; } = new();
        public List<object>? Array { get; set; }
        public bool IsTable => Array == null;
        public bool IsArray => Array != null;
    }
    
    private static readonly Dictionary<IntPtr, SimpleKeyValues3> _kv3Instances = new();
    private static IntPtr _nextKv3Handle = (IntPtr)10000;
    
    /// <summary>
    /// Convertit un JSON en KeyValues3 (émulation simplifiée).
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr EngineGlue_JsonToKeyValues3(IntPtr pJson)
    {
        if (pJson == IntPtr.Zero) return IntPtr.Zero;
        
        try
        {
            string? json = Marshal.PtrToStringUTF8(pJson);
            if (string.IsNullOrEmpty(json)) return IntPtr.Zero;
            
            // Parser le JSON et créer une structure KeyValues3 simple
            var kv3 = new SimpleKeyValues3();
            // Pour l'instant, on crée juste une instance vide car le parsing JSON complet est complexe
            // Le moteur utilisera plutôt LoadKeyValues3 pour charger depuis un fichier
            
            IntPtr handle = _nextKv3Handle;
            _nextKv3Handle = (IntPtr)((long)_nextKv3Handle + 1);
            
            lock (_kv3Instances)
            {
                _kv3Instances[handle] = kv3;
            }
            
            return handle;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }
    
    /// <summary>
    /// Convertit KeyValues en JSON (émulation simplifiée).
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr EngineGlue_KeyValuesToJson(IntPtr pKeyValues)
    {
        // KeyValues (ancien format) n'est plus utilisé, retourner null
        return IntPtr.Zero;
    }
    
    /// <summary>
    /// Convertit KeyValues3 en JSON (émulation simplifiée).
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr EngineGlue_KeyValues3ToJson(IntPtr pKeyValues3)
    {
        if (pKeyValues3 == IntPtr.Zero) return IntPtr.Zero;
        
        try
        {
            SimpleKeyValues3? kv3;
            lock (_kv3Instances)
            {
                if (!_kv3Instances.TryGetValue(pKeyValues3, out kv3) || kv3 == null)
                    return IntPtr.Zero;
            }
            
            // Convertir en JSON simple
            var jsonBuilder = new StringBuilder();
            jsonBuilder.Append('{');
            
            bool first = true;
            foreach (var kv in kv3.Data)
            {
                if (!first) jsonBuilder.Append(',');
                first = false;
                
                jsonBuilder.Append('"');
                jsonBuilder.Append(kv.Key.Replace("\"", "\\\""));
                jsonBuilder.Append("\":");
                
                if (kv.Value is string str)
                {
                    jsonBuilder.Append('"');
                    jsonBuilder.Append(str.Replace("\"", "\\\""));
                    jsonBuilder.Append('"');
                }
                else if (kv.Value is bool b)
                {
                    jsonBuilder.Append(b ? "true" : "false");
                }
                else if (kv.Value is int || kv.Value is long)
                {
                    jsonBuilder.Append(kv.Value);
                }
                else if (kv.Value is float || kv.Value is double)
                {
                    jsonBuilder.Append(kv.Value);
                }
                else
                {
                    jsonBuilder.Append('"');
                    jsonBuilder.Append(kv.Value?.ToString()?.Replace("\"", "\\\"") ?? "null");
                    jsonBuilder.Append('"');
                }
            }
            
            jsonBuilder.Append('}');
            
            string json = jsonBuilder.ToString();
            byte[] utf8Bytes = Encoding.UTF8.GetBytes(json);
            IntPtr ptr = Marshal.AllocHGlobal(utf8Bytes.Length + 1);
            Marshal.Copy(utf8Bytes, 0, ptr, utf8Bytes.Length);
            Marshal.WriteByte(ptr, utf8Bytes.Length, 0);
            
            lock (_allocatedStrings)
            {
                _allocatedStrings.Add(ptr);
            }
            
            return ptr;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }
    
    /// <summary>
    /// Charge KeyValues3 depuis un fichier ou une chaîne (émulation simplifiée).
    /// Parse le format KeyValues texte (commence par '<') et le convertit en structure KeyValues3.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr EngineGlue_LoadKeyValues3(IntPtr pPath)
    {
        if (pPath == IntPtr.Zero) return IntPtr.Zero;
        
        try
        {
            string? input = Marshal.PtrToStringUTF8(pPath);
            if (string.IsNullOrEmpty(input)) return IntPtr.Zero;
            
            // Si c'est un chemin de fichier, essayer de le lire
            string? content = null;
            if (File.Exists(input))
            {
                content = File.ReadAllText(input);
            }
            else if (input.TrimStart().StartsWith('<'))
            {
                // C'est déjà du contenu KeyValues
                content = input;
            }
            else
            {
                return IntPtr.Zero;
            }
            
            if (string.IsNullOrEmpty(content)) return IntPtr.Zero;
            
            // Parser le format KeyValues simple (format texte avec <key>value</key>)
            var kv3 = new SimpleKeyValues3();
            
            // Parser basique du format KeyValues
            // Format: <key>value</key> ou <key>...</key> pour les objets imbriqués
            int pos = 0;
            ParseKeyValues(content, ref pos, kv3.Data);
            
            IntPtr handle = _nextKv3Handle;
            _nextKv3Handle = (IntPtr)((long)_nextKv3Handle + 1);
            
            lock (_kv3Instances)
            {
                _kv3Instances[handle] = kv3;
            }
            
            return handle;
        }
        catch (Exception ex)
        {
            if (_verboseLogging)
            {
                Console.WriteLine($"[NativeAOT] EngineGlue_LoadKeyValues3 error: {ex.Message}");
            }
            return IntPtr.Zero;
        }
    }
    
    // Parser simple pour le format KeyValues texte
    private static void ParseKeyValues(string content, ref int pos, Dictionary<string, object> data)
    {
        while (pos < content.Length)
        {
            // Ignorer les espaces
            while (pos < content.Length && char.IsWhiteSpace(content[pos])) pos++;
            if (pos >= content.Length) break;
            
            // Chercher '<'
            if (content[pos] != '<') break;
            pos++;
            
            // Lire le nom de la clé
            int keyStart = pos;
            while (pos < content.Length && content[pos] != '>' && content[pos] != ' ') pos++;
            if (pos >= content.Length) break;
            
            string key = content.Substring(keyStart, pos - keyStart);
            
            // Chercher '>'
            while (pos < content.Length && content[pos] != '>') pos++;
            if (pos >= content.Length) break;
            pos++; // Skip '>'
            
            // Lire la valeur
            while (pos < content.Length && char.IsWhiteSpace(content[pos])) pos++;
            
            if (pos < content.Length && content[pos] == '<')
            {
                // C'est un objet imbriqué
                var nested = new Dictionary<string, object>();
                ParseKeyValues(content, ref pos, nested);
                data[key] = nested;
            }
            else
            {
                // C'est une valeur simple
                int valueStart = pos;
                while (pos < content.Length && content[pos] != '<') pos++;
                string value = content.Substring(valueStart, pos - valueStart).Trim();
                
                // Essayer de parser comme nombre ou booléen
                if (long.TryParse(value, out long longVal))
                {
                    data[key] = longVal;
                }
                else if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double doubleVal))
                {
                    data[key] = doubleVal;
                }
                else if (value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("1"))
                {
                    data[key] = true;
                }
                else if (value.Equals("false", StringComparison.OrdinalIgnoreCase) || value.Equals("0"))
                {
                    data[key] = false;
                }
                else
                {
                    data[key] = value;
                }
            }
            
            // Chercher '</key>'
            while (pos < content.Length && char.IsWhiteSpace(content[pos])) pos++;
            if (pos < content.Length && content[pos] == '<')
            {
                pos++; // Skip '<'
                if (pos < content.Length && content[pos] == '/')
                {
                    pos++; // Skip '/'
                    // Skip jusqu'à '>'
                    while (pos < content.Length && content[pos] != '>') pos++;
                    if (pos < content.Length) pos++; // Skip '>'
                }
            }
        }
    }
    
    /// <summary>
    /// Obtient un StringToken (identifiant unique) pour une chaîne.
    /// Utilise Sandbox.StringToken pour la compatibilité avec le système existant.
    /// </summary>
    [UnmanagedCallersOnly]
    public static uint EngineGlue_GetStringToken(IntPtr pString)
    {
        if (pString == IntPtr.Zero) return 0;
        
        string? str = Marshal.PtrToStringUTF8(pString);
        if (string.IsNullOrEmpty(str)) return 0;
        
        // Utiliser Sandbox.StringToken pour créer le token (utilise MurmurHash2)
        var token = new Sandbox.StringToken(str);
        return token.Value;
    }
    
    /// <summary>
    /// Obtient la chaîne correspondant à un StringToken.
    /// Alloue de la mémoire pour la chaîne et retourne un pointeur.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr EngineGlue_GetStringTokenValue(uint token)
    {
        if (token == 0) return IntPtr.Zero;
        
        // Utiliser Sandbox.StringToken pour récupérer la chaîne
        string? str = Sandbox.StringToken.GetValue(token);
        if (string.IsNullOrEmpty(str)) return IntPtr.Zero;
        
        // Allouer de la mémoire pour la chaîne UTF-8
        byte[] utf8Bytes = Encoding.UTF8.GetBytes(str);
        int size = utf8Bytes.Length + 1; // +1 pour le null terminator
        
        IntPtr ptr = Marshal.AllocHGlobal(size);
        Marshal.Copy(utf8Bytes, 0, ptr, utf8Bytes.Length);
        Marshal.WriteByte(ptr, utf8Bytes.Length, 0); // Null terminator
        
        // Garder une référence pour éviter le GC
        lock (_allocatedStrings)
        {
            _allocatedStrings.Add(ptr);
            _tokenStringPtrs[token] = ptr;
        }
        
        return ptr;
    }
    
    /// <summary>
    /// Ajoute un chemin de recherche au système de fichiers.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void EngineGlue_AddSearchPath(IntPtr pPath, IntPtr pPathId, int nFlags)
    {
        if (pPath == IntPtr.Zero) return;
        
        string? path = Marshal.PtrToStringUTF8(pPath);
        if (string.IsNullOrEmpty(path)) return;
        
        string? pathId = pPathId != IntPtr.Zero ? Marshal.PtrToStringUTF8(pPathId) : null;
        
        // Normaliser le chemin
        path = Path.GetFullPath(path);
        
        lock (_searchPaths)
        {
            if (_searchPaths.Add(path))
            {
                // Ajouter au système de fichiers natif si disponible
                try
                {
                    // Utiliser le système de fichiers managé
                    if (Directory.Exists(path))
                    {
                        // Le système de fichiers Zio gère déjà les chemins via BaseFileSystem
                        // On peut aussi essayer d'ajouter au système natif si disponible
                        if (_verboseLogging)
                        {
                            Console.WriteLine($"[NativeAOT] EngineGlue_AddSearchPath: {path} (id: {pathId ?? "null"})");
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (_verboseLogging)
                    {
                        Console.WriteLine($"[NativeAOT] EngineGlue_AddSearchPath failed: {ex.Message}");
                    }
                }
            }
        }
    }
    
    /// <summary>
    /// Retire un chemin de recherche du système de fichiers.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int EngineGlue_RemoveSearchPath(IntPtr pPath, IntPtr pPathId)
    {
        if (pPath == IntPtr.Zero) return 0;
        
        string? path = Marshal.PtrToStringUTF8(pPath);
        if (string.IsNullOrEmpty(path)) return 0;
        
        // Normaliser le chemin
        path = Path.GetFullPath(path);
        
        lock (_searchPaths)
        {
            if (_searchPaths.Remove(path))
            {
                if (_verboseLogging)
                {
                    Console.WriteLine($"[NativeAOT] EngineGlue_RemoveSearchPath: {path}");
                }
                return 1; // Success
            }
        }
        
        return 0; // Not found
    }
    
    /// <summary>
    /// Estime l'utilisation mémoire du processus.
    /// </summary>
    [UnmanagedCallersOnly]
    public static ulong EngineGlue_ApproximateProcessMemoryUsage()
    {
        try
        {
            // Utiliser GC.GetTotalMemory pour une estimation rapide
            long managedMemory = GC.GetTotalMemory(false);
            
            // Essayer d'obtenir la mémoire système si possible
            // Sur Linux, on pourrait lire /proc/self/status mais c'est complexe
            // Pour l'instant, retourner juste la mémoire managée
            return (ulong)managedMemory;
        }
        catch
        {
            return 0;
        }
    }
    
    /// <summary>
    /// Lit un fichier de ressource compilé en JSON depuis un buffer mémoire.
    /// Les fichiers de ressources compilés Source 2 peuvent être binaires ou JSON.
    /// On essaie de détecter le format et de retourner le JSON.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr EngineGlue_ReadCompiledResourceFileJson(IntPtr pData)
    {
        if (pData == IntPtr.Zero) return IntPtr.Zero;
        
        try
        {
            // Essayer de lire comme UTF-8
            // Les fichiers compilés Source 2 peuvent être binaires ou JSON
            
            // Lire les premiers bytes pour détecter le format
            byte firstByte = Marshal.ReadByte(pData);
            
            // Si ça commence par '{' ou '[', c'est probablement du JSON
            if (firstByte == '{' || firstByte == '[')
            {
                // Lire comme chaîne UTF-8 (null-terminated)
                string? json = Marshal.PtrToStringUTF8(pData);
                if (!string.IsNullOrEmpty(json))
                {
                    // Allouer et retourner une copie
                    byte[] utf8Bytes = Encoding.UTF8.GetBytes(json);
                    IntPtr ptr = Marshal.AllocHGlobal(utf8Bytes.Length + 1);
                    Marshal.Copy(utf8Bytes, 0, ptr, utf8Bytes.Length);
                    Marshal.WriteByte(ptr, utf8Bytes.Length, 0);
                    
                    lock (_allocatedStrings)
                    {
                        _allocatedStrings.Add(ptr);
                    }
                    
                    return ptr;
                }
            }
            
            // Si c'est binaire, essayer de trouver un bloc JSON dans le fichier
            // Format simplifié: chercher "JSON" ou "{" dans les premiers bytes
            // Pour l'instant, on retourne null si ce n'est pas du JSON pur
            return IntPtr.Zero;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }
    
    /// <summary>
    /// Lit un bloc d'un fichier de ressource compilé.
    /// Les fichiers de ressources compilés Source 2 sont des fichiers binaires avec des blocs nommés.
    /// Signature: delegate* unmanaged&lt; IntPtr, IntPtr, int*, IntPtr &gt;
    /// Note: Le premier paramètre est le blockName (string), le second est le buffer de données.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr EngineGlue_ReadCompiledResourceFileBlock(IntPtr pBlockName, IntPtr pData, int* pSize)
    {
        if (pBlockName == IntPtr.Zero || pData == IntPtr.Zero || pSize == null)
        {
            if (pSize != null) *pSize = 0;
            return IntPtr.Zero;
        }
        
        try
        {
            string? blockName = Marshal.PtrToStringUTF8(pBlockName);
            if (string.IsNullOrEmpty(blockName)) 
            {
                *pSize = 0;
                return IntPtr.Zero;
            }
            
            // Les fichiers compilés Source 2 sont binaires avec un format spécifique
            // Pour l'instant, on essaie de trouver le bloc dans le fichier
            // Format simplifié: chercher le bloc par nom dans les données
            
            // Si le bloc demandé est "JSON" ou "json", retourner les données comme JSON
            if (blockName.Equals("JSON", StringComparison.OrdinalIgnoreCase) || 
                blockName.Equals("json", StringComparison.OrdinalIgnoreCase))
            {
                // Retourner les données telles quelles
                // Calculer la taille (jusqu'au null terminator ou fin du buffer)
                int size = 0;
                byte* dataPtr = (byte*)pData;
                while (size < 1024 * 1024 && dataPtr[size] != 0) // Limite de sécurité
                {
                    size++;
                }
                
                if (size > 0)
                {
                    *pSize = size;
                    // Retourner un pointeur vers les données (pas de copie pour l'instant)
                    return pData;
                }
            }
            
            // Bloc non trouvé ou format non supporté
            *pSize = 0;
            return IntPtr.Zero;
        }
        catch
        {
            if (pSize != null) *pSize = 0;
            return IntPtr.Zero;
        }
    }
    
    /// <summary>
    /// Lit un fichier de ressource compilé en JSON depuis le système de fichiers.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr EngineGlue_ReadCompiledResourceFileJsonFromFilesystem(IntPtr pPath)
    {
        if (pPath == IntPtr.Zero) return IntPtr.Zero;
        
        try
        {
            string? path = Marshal.PtrToStringUTF8(pPath);
            if (string.IsNullOrEmpty(path)) return IntPtr.Zero;
            
            // Essayer de lire depuis le système de fichiers
            if (File.Exists(path))
            {
                byte[] data = File.ReadAllBytes(path);
                
                // Essayer de lire comme JSON (utiliser la logique inline)
                return ReadCompiledResourceFileJsonHelper(data);
            }
            
            // Essayer aussi avec les chemins de recherche
            lock (_searchPaths)
            {
                foreach (var searchPath in _searchPaths)
                {
                    string fullPath = Path.Combine(searchPath, path);
                    if (File.Exists(fullPath))
                    {
                        byte[] data = File.ReadAllBytes(fullPath);
                        return ReadCompiledResourceFileJsonHelper(data);
                    }
                }
            }
            
            return IntPtr.Zero;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }
    
    /// <summary>
    /// Active ou désactive le logging verbeux du moteur.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void EngineGlue_SetEngineLoggingVerbose(int bVerbose)
    {
        _verboseLogging = bVerbose != 0;
    }
    
    /// <summary>
    /// Demande un ticket d'authentification web (Steam).
    /// Sur Linux sans Steam, c'est un no-op.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void EngineGlue_RequestWebAuthTicket()
    {
        // Steam n'est pas disponible sur Linux dans cette émulation
        // Le moteur peut continuer sans ticket d'authentification
    }
    
    /// <summary>
    /// Annule une demande de ticket d'authentification web (Steam).
    /// Sur Linux sans Steam, c'est un no-op.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void EngineGlue_CancelWebAuthTicket()
    {
        // Steam n'est pas disponible sur Linux dans cette émulation
    }
    
    /// <summary>
    /// Obtient le ticket d'authentification web (Steam).
    /// Sur Linux sans Steam, retourne IntPtr.Zero.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr EngineGlue_GetWebAuthTicket()
    {
        // Steam n'est pas disponible sur Linux dans cette émulation
        return IntPtr.Zero;
    }
    
    // ============================================================================
    // Glue_Resources Functions (Glue_Resources_Get*)
    // Signatures exactes depuis Interop.Engine.cs ligne 7873-7877
    // Indices depuis Interop.Engine.cs ligne 16499-16503
    // ============================================================================
    
    // Cache pour les ressources chargées (évite de recharger plusieurs fois)
    private static readonly Dictionary<string, IntPtr> _resourceCache = new();
    
    // Compteurs pour les handles uniques (pattern identique à MaterialSystem)
    private static int _nextAnimationGraphId = 3000000;
    private static int _nextShaderId = 4000000;
    
    // Dictionnaires pour mapper les handles vers les données (pattern identique à MaterialSystem)
    // Note: _modelHandles est maintenant dans Model/ModelSystem.cs
    // Note: _textureHandles est maintenant dans Texture/TextureSystem.cs
    private static readonly Dictionary<IntPtr, AnimationGraphResourceData> _animationGraphHandles = new();
    private static readonly Dictionary<IntPtr, ShaderResourceData> _shaderHandles = new();
    
    /// <summary>
    /// Charge un matériau depuis une ressource.
    /// Signature exacte depuis Interop.Engine.cs: delegate* unmanaged&lt; IntPtr, IntPtr &gt;
    /// Retourne un handle vers IMaterial.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr Glue_Resources_GetMaterial(IntPtr pName)
    {
        if (pName == IntPtr.Zero) return IntPtr.Zero;
        
        string? resourceName = Marshal.PtrToStringUTF8(pName);
        if (string.IsNullOrEmpty(resourceName)) return IntPtr.Zero;
        
        // Vérifier le cache
        string cacheKey = $"material:{resourceName}";
        lock (_resourceCache)
        {
            if (_resourceCache.TryGetValue(cacheKey, out var cachedHandle))
            {
                return cachedHandle;
            }
        }
        
        // Utiliser MaterialSystem pour charger/créer le matériau
        // On appelle directement la fonction native via le système de matériaux
        IntPtr materialHandle = Material.MaterialSystem.FindOrCreateMaterialFromResourceHelper(resourceName);
        
        if (materialHandle != IntPtr.Zero)
        {
            lock (_resourceCache)
            {
                _resourceCache[cacheKey] = materialHandle;
            }
        }
        
        return materialHandle;
    }
    
    /// <summary>
    /// Charge une texture depuis une ressource.
    /// Signature exacte depuis Interop.Engine.cs: delegate* unmanaged&lt; IntPtr, IntPtr &gt;
    /// Retourne un handle vers ITexture.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr Glue_Resources_GetTexture(IntPtr pName)
    {
        if (pName == IntPtr.Zero) return IntPtr.Zero;
        
        string? resourceName = Marshal.PtrToStringUTF8(pName);
        if (string.IsNullOrEmpty(resourceName)) return IntPtr.Zero;
        
        // Vérifier le cache
        string cacheKey = $"texture:{resourceName}";
        lock (_resourceCache)
        {
            if (_resourceCache.TryGetValue(cacheKey, out var cachedHandle))
            {
                return cachedHandle;
            }
        }
        
        // Utiliser TextureSystem pour charger/créer la texture
        IntPtr textureHandle = Texture.TextureSystem.CreateTextureFromResourceHelper(resourceName);
        
        if (textureHandle != IntPtr.Zero)
        {
            lock (_resourceCache)
            {
                _resourceCache[cacheKey] = textureHandle;
            }
        }
        
        return textureHandle;
    }
    
    /// <summary>
    /// Charge un modèle depuis une ressource.
    /// Signature exacte depuis Interop.Engine.cs: delegate* unmanaged&lt; IntPtr, IntPtr &gt;
    /// Retourne un handle vers IModel.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr Glue_Resources_GetModel(IntPtr pName)
    {
        if (pName == IntPtr.Zero) return IntPtr.Zero;
        
        string? resourceName = Marshal.PtrToStringUTF8(pName);
        if (string.IsNullOrEmpty(resourceName)) return IntPtr.Zero;
        
        // Vérifier le cache
        string cacheKey = $"model:{resourceName}";
        lock (_resourceCache)
        {
            if (_resourceCache.TryGetValue(cacheKey, out var cachedHandle))
            {
                return cachedHandle;
            }
        }
        
        // Utiliser ModelSystem pour charger/créer le modèle
        // On appelle directement la fonction via le système de modèles
        IntPtr modelHandle = Model.ModelSystem.CreateModelFromResourceHelper(resourceName);
        
        if (modelHandle != IntPtr.Zero)
        {
            lock (_resourceCache)
            {
                _resourceCache[cacheKey] = modelHandle;
            }
        }
        
        return modelHandle;
    }
    
    /// <summary>
    /// Charge un graphe d'animation depuis une ressource.
    /// Signature exacte depuis Interop.Engine.cs: delegate* unmanaged&lt; IntPtr, IntPtr &gt;
    /// Retourne un handle vers HAnimationGraph.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr Glue_Resources_GetAnimationGraph(IntPtr pName)
    {
        if (pName == IntPtr.Zero) return IntPtr.Zero;
        
        string? resourceName = Marshal.PtrToStringUTF8(pName);
        if (string.IsNullOrEmpty(resourceName)) return IntPtr.Zero;
        
        // Vérifier le cache
        string cacheKey = $"animgraph:{resourceName}";
        lock (_resourceCache)
        {
            if (_resourceCache.TryGetValue(cacheKey, out var cachedHandle))
            {
                return cachedHandle;
            }
        }
        
        // Créer un graphe d'animation avec le pattern Sandbox (compatible avec IAnimationGraph_*)
        // Pattern identique à MaterialSystem : créer un handle unique, pas le BindingPtr
        var animGraphData = new AnimationGraphResourceData { Name = resourceName };
        
        // Enregistrer dans HandleManager pour obtenir un BindingPtr unique
        int bindingHandle = HandleManager.Register(animGraphData);
        animGraphData.BindingPtr = (IntPtr)bindingHandle;
        
        // Créer un handle unique pour le graphe d'animation (compatible avec HAnimationGraph)
        // Le handle doit être unique et mappé vers les données (pas le BindingPtr directement)
        IntPtr animGraphHandle = (IntPtr)_nextAnimationGraphId++;
        lock (_animationGraphHandles)
        {
            _animationGraphHandles[animGraphHandle] = animGraphData;
        }
        
        lock (_resourceCache)
        {
            _resourceCache[cacheKey] = animGraphHandle;
        }
        
        Console.WriteLine($"[NativeAOT] Glue_Resources_GetAnimationGraph: {resourceName} -> handle={animGraphHandle}, binding={bindingHandle}");
        return animGraphHandle;
    }
    
    /// <summary>
    /// Charge un shader depuis une ressource.
    /// Signature exacte depuis Interop.Engine.cs: delegate* unmanaged&lt; IntPtr, IntPtr &gt;
    /// Retourne un handle vers CVfx.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr Glue_Resources_GetShader(IntPtr pName)
    {
        if (pName == IntPtr.Zero) return IntPtr.Zero;
        
        string? resourceName = Marshal.PtrToStringUTF8(pName);
        if (string.IsNullOrEmpty(resourceName)) return IntPtr.Zero;
        
        // Vérifier le cache
        string cacheKey = $"shader:{resourceName}";
        lock (_resourceCache)
        {
            if (_resourceCache.TryGetValue(cacheKey, out var cachedHandle))
            {
                return cachedHandle;
            }
        }
        
        // Créer un shader avec le pattern Sandbox (compatible avec CVfx_*)
        // Pattern identique à MaterialSystem : créer un handle unique, pas le BindingPtr
        var shaderData = new ShaderResourceData { Name = resourceName };
        
        // Enregistrer dans HandleManager pour obtenir un BindingPtr unique
        int bindingHandle = HandleManager.Register(shaderData);
        shaderData.BindingPtr = (IntPtr)bindingHandle;
        
        // Créer un handle unique pour le shader (compatible avec CVfx)
        // Le handle doit être unique et mappé vers les données (pas le BindingPtr directement)
        IntPtr shaderHandle = (IntPtr)_nextShaderId++;
        lock (_shaderHandles)
        {
            _shaderHandles[shaderHandle] = shaderData;
        }
        
        lock (_resourceCache)
        {
            _resourceCache[cacheKey] = shaderHandle;
        }
        
        Console.WriteLine($"[NativeAOT] Glue_Resources_GetShader: {resourceName} -> handle={shaderHandle}, binding={bindingHandle}");
        return shaderHandle;
    }
    
    // Classes internes pour stocker les données des ressources
    // Pattern inspiré de MaterialData dans MaterialSystem.cs
    // Compatible avec le système Sandbox qui utilise ReferenceCount et BindingPtr
    // Note: TextureResourceData est maintenant dans Texture/TextureSystem.cs (TextureData)
    
    // Note: ModelResourceData est maintenant dans Model/ModelSystem.cs (ModelData)
    
    private class AnimationGraphResourceData
    {
        public string Name { get; set; } = "";
        public int ReferenceCount { get; set; } = 1; // Compteur de références pour les handles forts
        public IntPtr BindingPtr { get; set; } = IntPtr.Zero; // Pointeur de binding unique (handle HandleManager)
        public bool IsError { get; set; } = false;
        public bool IsLoaded { get; set; } = true; // Par défaut, considéré comme chargé
    }
    
    private class ShaderResourceData
    {
        public string Name { get; set; } = "";
        public int ReferenceCount { get; set; } = 1; // Compteur de références pour les handles forts
        public IntPtr BindingPtr { get; set; } = IntPtr.Zero; // Pointeur de binding unique (handle HandleManager)
        public bool IsError { get; set; } = false;
        public bool IsLoaded { get; set; } = true; // Par défaut, considéré comme chargé
    }
    
    // ============================================
    // Glue_Networking functions (Steam Networking)
    // ============================================
    // Ces fonctions sont liées à Steam Networking et peuvent être désactivées si Steam n'est pas disponible.
    
    /// <summary>
    /// Exécute les callbacks Steam Networking.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void Glue_Networking_RunCallbacks()
    {
        if (!Steam.SteamAPI.IsSteamEnabled())
        {
            // Steam désactivé, rien à faire (pas d'exception car c'est appelé fréquemment)
            return;
        }
        throw new NotImplementedException("Glue_Networking_RunCallbacks: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    /// <summary>
    /// Définit la fonction de debug pour Steam Networking.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void Glue_Networking_SetDebugFunction(int level, IntPtr func)
    {
        // Si Steam est désactivé, rien à faire (no-op)
        if (!Steam.SteamAPI.IsSteamEnabled()) return;
        throw new NotImplementedException("Glue_Networking_SetDebugFunction: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    /// <summary>
    /// Obtient le statut d'authentification Steam Networking.
    /// </summary>
    [UnmanagedCallersOnly]
    public static long Glue_Networking_GetAuthenticationStatus(IntPtr debugMsg)
    {
        // Si Steam est désactivé, retourner 0 (pas d'exception)
        if (!Steam.SteamAPI.IsSteamEnabled()) return 0;
        throw new NotImplementedException("Glue_Networking_GetAuthenticationStatus: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    /// <summary>
    /// Obtient le statut du réseau relais Steam Networking.
    /// </summary>
    [UnmanagedCallersOnly]
    public static long Glue_Networking_GetRelayNetworkStatus(IntPtr debugMsg)
    {
        // Si Steam est désactivé, retourner 0 (pas d'exception)
        if (!Steam.SteamAPI.IsSteamEnabled()) return 0;
        throw new NotImplementedException("Glue_Networking_GetRelayNetworkStatus: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    /// <summary>
    /// Crée un socket Steam Networking.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr Glue_Networking_CreateSocket(int virtualPort)
    {
        // Si Steam est désactivé, retourner IntPtr.Zero (pas d'exception)
        if (!Steam.SteamAPI.IsSteamEnabled()) return IntPtr.Zero;
        throw new NotImplementedException("Glue_Networking_CreateSocket: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    /// <summary>
    /// Crée un socket Steam Networking basé sur IP.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr Glue_Networking_CreateIpBasedSocket(int useFakeIP)
    {
        // Si Steam est désactivé, retourner IntPtr.Zero (pas d'exception)
        if (!Steam.SteamAPI.IsSteamEnabled()) return IntPtr.Zero;
        throw new NotImplementedException("Glue_Networking_CreateIpBasedSocket: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    /// <summary>
    /// Ferme un socket Steam Networking.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void Glue_Networking_CloseSocket(IntPtr socket)
    {
        // Si Steam est désactivé, rien à faire (no-op)
        if (!Steam.SteamAPI.IsSteamEnabled()) return;
        throw new NotImplementedException("Glue_Networking_CloseSocket: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    /// <summary>
    /// Obtient l'adresse d'un socket Steam Networking.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr Glue_Networking_GetSocketAddress(IntPtr socket)
    {
        // Si Steam est désactivé, retourner IntPtr.Zero (pas d'exception)
        if (!Steam.SteamAPI.IsSteamEnabled()) return IntPtr.Zero;
        throw new NotImplementedException("Glue_Networking_GetSocketAddress: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    /// <summary>
    /// Démarre une requête asynchrone pour obtenir une FakeIP.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void Glue_Networking_BeginAsyncRequestFakeIP()
    {
        // Si Steam est désactivé, rien à faire (no-op)
        if (!Steam.SteamAPI.IsSteamEnabled()) return;
        throw new NotImplementedException("Glue_Networking_BeginAsyncRequestFakeIP: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    /// <summary>
    /// Obtient l'identité Steam Networking.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr Glue_Networking_GetIdentity()
    {
        // Si Steam est désactivé, retourner IntPtr.Zero (pas d'exception)
        if (!Steam.SteamAPI.IsSteamEnabled()) return IntPtr.Zero;
        throw new NotImplementedException("Glue_Networking_GetIdentity: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    /// <summary>
    /// Crée un groupe de polling Steam Networking.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr Glue_Networking_CreatePollGroup()
    {
        // Si Steam est désactivé, retourner IntPtr.Zero (pas d'exception)
        if (!Steam.SteamAPI.IsSteamEnabled()) return IntPtr.Zero;
        throw new NotImplementedException("Glue_Networking_CreatePollGroup: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    /// <summary>
    /// Détruit un groupe de polling Steam Networking.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void Glue_Networking_DestroyPollGroup(IntPtr group)
    {
        // Si Steam est désactivé, rien à faire (no-op)
        if (!Steam.SteamAPI.IsSteamEnabled()) return;
        throw new NotImplementedException("Glue_Networking_DestroyPollGroup: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    /// <summary>
    /// Définit le groupe de polling pour une connexion Steam Networking.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void Glue_Networking_SetPollGroup(IntPtr connection, IntPtr group)
    {
        // Si Steam est désactivé, rien à faire (no-op)
        if (!Steam.SteamAPI.IsSteamEnabled()) return;
        throw new NotImplementedException("Glue_Networking_SetPollGroup: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    /// <summary>
    /// Obtient les messages d'un groupe de polling Steam Networking.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int Glue_Networking_GetPollGroupMessages(IntPtr group, IntPtr array_of_pointers, int maxmessages)
    {
        // Si Steam est désactivé, retourner 0 (pas d'exception)
        if (!Steam.SteamAPI.IsSteamEnabled()) return 0;
        throw new NotImplementedException("Glue_Networking_GetPollGroupMessages: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    /// <summary>
    /// Se connecte à un SteamID via Steam Networking.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr Glue_Networking_ConnectToSteamId(ulong steamid, int virtualPort)
    {
        // Si Steam est désactivé, retourner IntPtr.Zero (pas d'exception)
        if (!Steam.SteamAPI.IsSteamEnabled()) return IntPtr.Zero;
        throw new NotImplementedException("Glue_Networking_ConnectToSteamId: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    /// <summary>
    /// Se connecte à une adresse IP via Steam Networking.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr Glue_Networking_ConnectToIpAddress(IntPtr address)
    {
        // Si Steam est désactivé, retourner IntPtr.Zero (pas d'exception)
        if (!Steam.SteamAPI.IsSteamEnabled()) return IntPtr.Zero;
        throw new NotImplementedException("Glue_Networking_ConnectToIpAddress: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    /// <summary>
    /// Ferme une connexion Steam Networking.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void Glue_Networking_CloseConnection(IntPtr c, int reason, IntPtr debugReason)
    {
        // Si Steam est désactivé, rien à faire (no-op)
        if (!Steam.SteamAPI.IsSteamEnabled()) return;
        throw new NotImplementedException("Glue_Networking_CloseConnection: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    /// <summary>
    /// Accepte une connexion Steam Networking.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void Glue_Networking_AcceptConnection(IntPtr c)
    {
        // Si Steam est désactivé, rien à faire (no-op)
        if (!Steam.SteamAPI.IsSteamEnabled()) return;
        throw new NotImplementedException("Glue_Networking_AcceptConnection: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    /// <summary>
    /// Vide les messages en attente sur une connexion Steam Networking.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void Glue_Networking_FlushMessagesOnConnection(IntPtr c)
    {
        // Si Steam est désactivé, rien à faire (no-op)
        if (!Steam.SteamAPI.IsSteamEnabled()) return;
        throw new NotImplementedException("Glue_Networking_FlushMessagesOnConnection: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    /// <summary>
    /// Envoie un message via Steam Networking.
    /// </summary>
    [UnmanagedCallersOnly]
    public static long Glue_Networking_SendMessage(IntPtr c, IntPtr data, int length, int flags)
    {
        // Si Steam est désactivé, retourner 0 (pas d'exception)
        if (!Steam.SteamAPI.IsSteamEnabled()) return 0;
        throw new NotImplementedException("Glue_Networking_SendMessage: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    /// <summary>
    /// Obtient les messages d'une connexion Steam Networking.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int Glue_Networking_GetConnectionMessages(IntPtr c, IntPtr array_of_pointers, int maxmessages)
    {
        // Si Steam est désactivé, retourner 0 (pas d'exception)
        if (!Steam.SteamAPI.IsSteamEnabled()) return 0;
        throw new NotImplementedException("Glue_Networking_GetConnectionMessages: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    /// <summary>
    /// Obtient l'état d'une connexion Steam Networking.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int Glue_Networking_GetConnectionState(IntPtr c)
    {
        // Si Steam est désactivé, retourner 0 (pas d'exception)
        if (!Steam.SteamAPI.IsSteamEnabled()) return 0;
        throw new NotImplementedException("Glue_Networking_GetConnectionState: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    /// <summary>
    /// Obtient la description d'une connexion Steam Networking.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr Glue_Networking_GetConnectionDescription(IntPtr c)
    {
        // Si Steam est désactivé, retourner IntPtr.Zero (pas d'exception)
        if (!Steam.SteamAPI.IsSteamEnabled()) return IntPtr.Zero;
        throw new NotImplementedException("Glue_Networking_GetConnectionDescription: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    /// <summary>
    /// Obtient le SteamID d'une connexion Steam Networking.
    /// </summary>
    [UnmanagedCallersOnly]
    public static ulong Glue_Networking_GetConnectionSteamId(IntPtr c)
    {
        // Si Steam est désactivé, retourner 0 (pas d'exception)
        if (!Steam.SteamAPI.IsSteamEnabled()) return 0;
        throw new NotImplementedException("Glue_Networking_GetConnectionSteamId: Steam Networking not yet implemented in the linux emulation layer");
    }
}
