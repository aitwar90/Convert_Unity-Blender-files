using UnityEngine;
using UnityEditor;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;

/// <summary>
/// System automatyzujący pomost między środowiskiem Unity a Blenderem. 
/// Służy do masowej konwersji meshy FBX/OBJ na format .blend (proceduralne pliki źródłowe).
/// <para><b>Główne zadania:</b></para>
/// <list type="number">
///     <item><description>Zbieranie danych o materiałach i teksturach bezpośrednio z komponentów MeshRenderer w Unity.</description></item>
///     <item><description>Generowanie tymczasowych plików instrukcji dla Blendera (Smart Batching).</description></item>
///     <item><description>Zdalne wywoływanie instancji Blendera w trybie Background.</description></item>
///     <item><description>Automatyczna podmiana referencji w prefabach — zamiana surowych meshy na dane pochodzące z wygenerowanych plików .blend.</description></item>
/// </list>
/// Wspiera systemy Windows oraz Linux, obsługując różnice w ścieżkach dostępu do binariów Blendera.
/// </summary>
public class BlenderBridge : EditorWindow
{
    /// <summary>
    /// Ścieżka do folderu z surowymi zasobami (FBX/OBJ), które wymagają przetworzenia.
    /// </summary>
    private string sourceFolder = "Assets/AssetyImportowane_Objekty";
    /// <summary>
    /// Folder docelowy, w którym zostaną utworzone pliki .blend oraz zaktualizowane prefaby.
    /// </summary>
    private string targetFolder = "Assets/AssetyPrzerobione";
    /// <summary>
    /// Ścieżka do pliku wykonywalnego Blendera dla systemów z rodziny Linux.
    /// </summary>
    private string blenderPathLinux = "blender";
    /// <summary>
    /// Ścieżka do pliku wykonywalnego Blendera dla systemów Windows.
    /// </summary>
    private string blenderPathWin = @"C:\Program Files\Blender Foundation\Blender 5.0\blender.exe";

    /// <summary>
    /// Wyświetla okno narzędzia w edytorze Unity.
    /// </summary>
    [MenuItem("Tools/3. Pełna Automatyzacja: Multi-Mesh FBX")]
    public static void ShowWindow() => GetWindow<BlenderBridge>("Krok 3: Blender Bridge");

    /// <summary>
    /// Rysuje interfejs konfiguracyjny, pozwalając na dostosowanie ścieżek do binariów Blendera zależnie od platformy.
    /// </summary>
    private void OnGUI()
    {
        GUILayout.Label("Ustawienia Workflow", EditorStyles.boldLabel);
        if (Application.platform == RuntimePlatform.LinuxEditor)
            blenderPathLinux = EditorGUILayout.TextField("Blender (Linux):", blenderPathLinux);
        else
            blenderPathWin = EditorGUILayout.TextField("Blender (Windows):", blenderPathWin);

        if (GUILayout.Button("Uruchom Workflow (Smart Batching)")) ProcessAll();
    }

    /// <summary>
    /// Reprezentuje pojedynczy proces wsadowy dla konkretnego pliku modelu.
    /// Grupuje wszystkie sub-obiekty i ich specyficzne dane materiałowe w celu uniknięcia wielokrotnego otwierania Blendera.
    /// </summary>
    private class MeshBatch
    {
        /// <summary>Ścieżka do oryginalnego pliku źródłowego FBX/OBJ.</summary>
        public string OriginalMeshPath; // np. importowane/Lamps1.fbx
        /// <summary>Ścieżka docelowa, pod którą zostanie zapisany wygenerowany plik .blend.</summary>
        public string TargetBlendPath;  // np. przerobione/Lamps1.blend
        /// <summary>
        /// Słownik przechowujący dane o sub-obiektach wewnątrz modelu.
        /// Klucz: Nazwa obiektu (Node name).
        /// Wartość: Lista sformatowanych ciągów tekstowych z danymi materiałów (Nazwa|Albedo|Normal).
        /// </summary>
        public Dictionary<string, List<string>> SubObjects = new Dictionary<string, List<string>>();
    }

    /// <summary>
    /// Główna pętla sterująca procesem konwersji.
    /// <list type="number">
    ///     <item><description>Buduje strukturę danych <see cref="MeshBatch"/> na podstawie analizy prefabów.</description></item>
    ///     <item><description>Generuje pliki tymczasowe z argumentami dla Blendera.</description></item>
    ///     <item><description>Inicjuje zewnętrzne procesy konwersji.</description></item>
    ///     <item><description>Wywołuje system podmiany meshy w prefabach.</description></item>
    /// </list>
    /// </summary>
    private void ProcessAll()
    {
        string bPath = (Application.platform == RuntimePlatform.LinuxEditor) ? blenderPathLinux : blenderPathWin;
        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { targetFolder });

        Dictionary<string, MeshBatch> batches = new Dictionary<string, MeshBatch>();

        // KROK 1: Budowanie struktury danych z prefabów
        foreach (string guid in prefabGuids)
        {
            string prefabPath = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

            var renderers = prefab.GetComponentsInChildren<MeshRenderer>(true);
            foreach (var ren in renderers)
            {
                MeshFilter mf = ren.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;

                string meshPath = AssetDatabase.GetAssetPath(mf.sharedMesh);

                // Ignorujemy meshe już przerobione lub domyślne kostki/sfery Unity
                if (!meshPath.StartsWith(sourceFolder)) continue;

                if (!batches.ContainsKey(meshPath))
                {
                    // Zamiast .Replace(".fbx", ".blend").Replace(".obj", ".blend")
                    string pathWithNewFolder = meshPath.Replace(sourceFolder, targetFolder);
                    batches[meshPath] = new MeshBatch
                    {
                        OriginalMeshPath = meshPath,
                        TargetBlendPath = Path.ChangeExtension(pathWithNewFolder, ".blend")
                    };
                }

                // Zbieramy materiały dla sub-obiektu. Używamy nazwy obiektu z prefabu, 
                // która w 99% odpowiada nazwie węzła w FBX.
                string objName = ren.gameObject.name;
                if (!batches[meshPath].SubObjects.ContainsKey(objName))
                {
                    List<string> matData = new List<string>();
                    foreach (var mat in ren.sharedMaterials)
                    {
                        if (mat == null) continue;
                        matData.Add($"{mat.name}|{GetTexturePath(mat, "_BaseMap", "_MainTex")}|{GetTexturePath(mat, "_BumpMap")}");
                    }
                    batches[meshPath].SubObjects[objName] = matData;
                }
            }
        }

        // KROK 2: Wywołanie Blendera dla każdego kontenera FBX
        foreach (var batch in batches.Values)
        {
            // Przygotowanie pliku tymczasowego dla pythona (omijamy limit znaków w terminalu)
            string tempFilePath = Path.Combine(Application.temporaryCachePath, "blender_args.txt");
            using (StreamWriter writer = new StreamWriter(tempFilePath))
            {
                writer.WriteLine(Path.GetFullPath(batch.OriginalMeshPath));
                writer.WriteLine(Path.GetFullPath(batch.TargetBlendPath));
                foreach (var kvp in batch.SubObjects)
                {
                    writer.WriteLine($"{kvp.Key}:{string.Join(";", kvp.Value)}");
                }
            }

            ExecuteBlender(bPath, tempFilePath);

            AssetDatabase.ImportAsset(batch.TargetBlendPath);
            UpdatePrefabMeshes(batch, prefabGuids);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        UnityEngine.Debug.Log("<color=cyan>Workflow zakończony: Inteligentny proces wsadowy wykonany!</color>");
    }

    /// <summary>
    /// Dokonuje podmiany referencji <see cref="MeshFilter.sharedMesh"/> w prefabach znajdujących się w folderze docelowym.
    /// </summary>
    /// <param name="batch">Dane o przetworzonym właśnie modelu.</param>
    /// <param name="allTargetPrefabs">Lista GUID-ów prefabów do przeskanowania i aktualizacji.</param>
    private void UpdatePrefabMeshes(MeshBatch batch, string[] allTargetPrefabs)
    {
        //AssetDatabase.Refresh();
        Object[] blendAssets = AssetDatabase.LoadAllAssetsAtPath(batch.TargetBlendPath);
        var blendMeshes = blendAssets.OfType<Mesh>().ToDictionary(m => m.name, m => m);

        foreach (string guid in allTargetPrefabs)
        {
            string prefabPath = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
            bool modified = false;

            var meshFilters = prefabRoot.GetComponentsInChildren<MeshFilter>(true);
            foreach (var mf in meshFilters)
            {
                if (mf.sharedMesh == null) continue;

                string currentMeshPath = AssetDatabase.GetAssetPath(mf.sharedMesh);
                if (currentMeshPath == batch.OriginalMeshPath)
                {
                    // Szukamy odpowiednika mesha w zaimportowanym pliku .blend
                    if (blendMeshes.TryGetValue(mf.sharedMesh.name, out Mesh newMesh))
                    {
                        mf.sharedMesh = newMesh;
                        modified = true;
                    }
                }
            }

            if (modified) PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }
    /// <summary>
    /// Pomocnik pobierający fizyczną ścieżkę do tekstury przypisanej do konkretnej właściwości materiału.
    /// </summary>
    /// <param name="mat">Materiał źródłowy.</param>
    /// <param name="propertyNames">Lista nazw właściwości do sprawdzenia (np. _BaseMap, _MainTex).</param>
    /// <returns>Pełna ścieżka systemowa do tekstury lub ciąg "NONE".</returns>
    private string GetTexturePath(Material mat, params string[] propertyNames)
    {
        foreach (string prop in propertyNames)
        {
            if (mat.HasProperty(prop) && mat.GetTexture(prop) != null)
                return Path.GetFullPath(AssetDatabase.GetAssetPath(mat.GetTexture(prop)));
        }
        return "NONE";
    }
    /// <summary>
    /// Uruchamia proces Blendera w trybie tła (Background Mode) z przekazaniem pliku instrukcji.
    /// </summary>
    /// <param name="bPath">Ścieżka do binariów Blendera.</param>
    /// <param name="tempFilePath">Ścieżka do pliku tymczasowego z argumentami.</param>
    private void ExecuteBlender(string bPath, string tempFilePath)
    {
        string scriptPath = Path.GetDirectoryName(new StackTrace(true).GetFrame(0).GetFileName());
        string pyScript = Path.Combine(scriptPath, "blender_setup_logic.py");

        string args = $"-b -P \"{pyScript}\" -- \"{tempFilePath}\"";

        ProcessStartInfo si = new ProcessStartInfo(bPath, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using (Process p = Process.Start(si))
        {
            p.WaitForExit();
            if (p.ExitCode != 0) UnityEngine.Debug.LogError(p.StandardError.ReadToEnd());
        }
    }
}