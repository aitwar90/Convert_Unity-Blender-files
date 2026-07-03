using UnityEngine;
using UnityEditor;
using System.IO;
using System;

/// <summary>
/// Pierwszy etap pipeline'u: System "Głębokiej Izolacji" zasobów.
/// Klasa odpowiada za rekurencyjne przepisanie struktury prefabów w celu całkowitego odpięcia ich 
/// od oryginalnych materiałów i tekstur dostarczonych przez zewnętrznych twórców.
/// </summary>
/// <remarks>
/// Kluczową funkcją jest stworzenie nowej hierarchii obiektów w folderze docelowym, która:
/// <list type="number">
///     <item><description>Zachowuje referencję do oryginalnego mesha (FBX), aby nie dublować danych binarnych.</description></item>
///     <item><description>Tworzy unikalne, edytowalne kopie materiałów i tekstur.</description></item>
///     <item><description>Gwarantuje, że zmiany w shaderach w folderze "Przerobione" nie wpłyną na pliki źródłowe.</description></item>
/// </list>
/// </remarks>
public class TextureAutomator : EditorWindow
{
    /// <summary>Ścieżka do surowych, nienaruszonych assetów (np. z Asset Store).</summary>
    private string sourcePath = "Assets/AssetyImportowane_Objekty";
    /// <summary>Ścieżka, gdzie powstanie "czysta" wersja projektu gotowa do automatyzacji w Blenderze.</summary>
    private string destinationPath = "Assets/AssetyPrzerobione";

    /// <summary>
    /// Inicjalizuje okno narzędzia w edytorze.
    /// </summary>
    [MenuItem("Tools/1. Głęboka Rekonstrukcja Assetów")]
    public static void ShowWindow() => GetWindow<TextureAutomator>("Krok 1: Rekonstrukcja");

    private void OnGUI()
    {
        GUILayout.Label("Ustawienia Głębokiej Migracji", EditorStyles.boldLabel);
        sourcePath = EditorGUILayout.TextField("Folder Źródłowy:", sourcePath);
        destinationPath = EditorGUILayout.TextField("Folder Docelowy:", destinationPath);

        if (GUILayout.Button("Uruchom Rekonstrukcję"))
        {
            if (EditorUtility.DisplayDialog("Potwierdzenie", "Czy przeprowadzić rekonstrukcję (bez kopiowania meshy)?", "Tak", "Anuluj"))
                RunDeepMigration();
        }
    }
    /// <summary>
    /// Główna procedura sterująca migracją. Odpowiada za czyszczenie folderu docelowego, 
    /// odnalezienie wszystkich prefabów w źródle i zainicjowanie procesu ich klonowania.
    /// </summary>
    private void RunDeepMigration()
    {
        PrepareFolder(destinationPath);
        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { sourcePath });

        foreach (string guid in prefabGuids)
        {
            string oldPrefabPath = AssetDatabase.GUIDToAssetPath(guid);
            GameObject oldPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(oldPrefabPath);
            if (oldPrefab == null) continue;

            GameObject newRoot = new GameObject(oldPrefab.name);
            CopyStructure(oldPrefab.transform, newRoot.transform);

            string newPrefabPath = oldPrefabPath.Replace(sourcePath, destinationPath);
            Directory.CreateDirectory(Path.GetDirectoryName(newPrefabPath));

            PrefabUtility.SaveAsPrefabAsset(newRoot, newPrefabPath);
            DestroyImmediate(newRoot);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("<color=green>Rekonstrukcja zakończona: Utworzono czyste prefaby korzystające z oryginalnych FBX.</color>");
    }
    /// <summary>
    /// Realizuje rekurencyjną rekonstrukcję hierarchii Transformów oraz komponentów renderujących.
    /// Jest to proces krytyczny, w którym następuje rozdzielenie logiki obiektu od jego źródłowych zależności.
    /// </summary>
    /// <param name="source">Transform źródłowy (z oryginalnego prefaba).</param>
    /// <param name="target">Transform docelowy (w nowo budowanym prefabie).</param>
    /// <remarks>
    /// <b>Przebieg operacji wewnątrz metody:</b>
    /// <list type="bullet">
    ///     <item>
    ///         <term>Kopiowanie Transformacji:</term>
    ///         <description>Przenosi dane lokalne (Position, Rotation, Scale), zapewniając, że struktura wizualna pozostanie identyczna.</description>
    ///     </item>
    ///     <item>
    ///         <term>Transfer Geometrii (Mesh):</term>
    ///         <description>
    ///             Dodaje <see cref="MeshFilter"/> i podpina <c>sharedMesh</c> z oryginału. 
    ///             W tym kroku celowo utrzymujemy referencję do źródłowego pliku FBX, aby uniknąć redundancji danych przed konwersją w Blenderze.
    ///         </description>
    ///     </item>
    ///     <item>
    ///         <term>Rekonstrukcja Renderingu (Materials):</term>
    ///         <description>
    ///             Inicjuje <see cref="MeshRenderer"/>. Zamiast kopiować tablicę materiałów jako referencję, metoda iteruje przez każdy slot, 
    ///             wywołując <see cref="GetOrCreateCleanMaterial"/>. Powoduje to fizyczne wytworzenie nowej instancji materiału na dysku 
    ///             dla każdego unikalnego materiału źródłowego.
    ///         </description>
    ///     </item>
    ///     <item>
    ///         <term>Rekurencja Hierarchii:</term>
    ///         <description>
    ///             Dla każdego dziecka w obiekcie źródłowym tworzy nowy, pusty GameObject, osadza go w nowej hierarchii 
    ///             i wywołuje samą siebie, aż do przetworzenia całego "drzewa" obiektu.
    ///         </description>
    ///     </item>
    /// </list>
    /// </remarks>
    private void CopyStructure(Transform source, Transform target)
    {
        try
        {
            target.localPosition = source.localPosition;
            target.localRotation = source.localRotation;
            target.localScale = source.localScale;

            // Bierzemy oryginalny Mesh (nie kopiujemy pliku .fbx)
            MeshFilter sourceMf = source.GetComponent<MeshFilter>();
            if (sourceMf != null && sourceMf.sharedMesh != null)
            {
                MeshFilter targetMf = target.gameObject.AddComponent<MeshFilter>();
                targetMf.sharedMesh = sourceMf.sharedMesh;
            }

            MeshRenderer sourceMr = source.GetComponent<MeshRenderer>();
            if (sourceMr != null)
            {
                MeshRenderer targetMr = target.gameObject.AddComponent<MeshRenderer>();
                Material[] sourceMats = sourceMr.sharedMaterials;
                Material[] targetMats = new Material[sourceMats.Length];

                for (int i = 0; i < sourceMats.Length; i++)
                {
                    if (sourceMats[i] != null)
                        targetMats[i] = GetOrCreateCleanMaterial(sourceMats[i]);
                }
                targetMr.sharedMaterials = targetMats;
            }

            foreach (Transform child in source)
            {
                GameObject newChild = new GameObject(child.name);
                newChild.transform.SetParent(target);
                CopyStructure(child, newChild.transform);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Błąd podczas kopiowania struktury obiektu {e.ToString()}");
        }
    }
    /// <summary>
    /// Zarządza cyklem życia materiału w nowym ekosystemie. Pełni rolę fabryki "czystych" materiałów, 
    /// izolując je od oryginalnych zasobów źródłowych.
    /// </summary>
    /// <param name="original">Referencja do oryginalnego materiału znajdującego się w folderze źródłowym.</param>
    /// <returns>
    /// Zwraca referencję do nowo utworzonego (lub już istniejącego w folderze docelowym) materiału. 
    /// Jeśli materiał źródłowy znajduje się poza <see cref="sourcePath"/>, zwraca oryginał bez zmian.
    /// </returns>
    /// <remarks>
    /// <b>Logika operacji:</b>
    /// <list type="number">
    ///     <item><description>Weryfikacja ścieżki: Sprawdza, czy materiał kwalifikuje się do migracji.</description></item>
    ///     <item><description>Instancjonowanie: Tworzy nową instancję materiału w pamięci operacyjnej na bazie oryginału.</description></item>
    ///     <item><description>Persystencja: Zapisuje materiał jako asset <c>.mat</c> w zmapowanej strukturze folderów docelowych.</description></item>
    ///     <item><description>Inicjacja kaskady: Wywołuje <see cref="UpdateTexturesForNewMaterial"/>, aby "oczyścić" mapy tekstur.</description></item>
    /// </list>
    /// </remarks>
    private Material GetOrCreateCleanMaterial(Material original)
    {
        string oldPath = AssetDatabase.GetAssetPath(original);
        if (!oldPath.StartsWith(sourcePath)) return original;

        string newPath = oldPath.Replace(sourcePath, destinationPath);
        Material existingMat = AssetDatabase.LoadAssetAtPath<Material>(newPath);
        if (existingMat != null) return existingMat;

        Directory.CreateDirectory(Path.GetDirectoryName(newPath));
        Material newMat = new Material(original);
        AssetDatabase.CreateAsset(newMat, newPath);
        UpdateTexturesForNewMaterial(newMat);

        return newMat;
    }
    /// <summary>
    /// Dokonuje głębokiej introspekcji Shadera (Shader Introspection). Przeszukuje definicję 
    /// shadera w poszukiwaniu wszystkich właściwości typu Texture.
    /// </summary>
    /// <param name="mat">Materiał docelowy, którego referencje do tekstur mają zostać podmienione na kopie.</param>
    /// <remarks>
    /// Metoda działa dynamicznie — nie jest przypisana do konkretnego typu shadera (Standard, URP/Lit itp.). 
    /// Iteruje po wszystkich slotach tekstur (np. Base Map, Normal Map, Metallic, Emission) i dla każdej 
    /// znalezionej tekstury wymusza proces migracji binarnej przez <see cref="GetOrCreateCleanTexture"/>.
    /// </remarks>
    private void UpdateTexturesForNewMaterial(Material mat)
    {
        int propertyCount = mat.shader.GetPropertyCount();
        for (int i = 0; i < propertyCount; i++)
        {
            if (mat.shader.GetPropertyType(i) == UnityEngine.Rendering.ShaderPropertyType.Texture)
            {
                string propName = mat.shader.GetPropertyName(i);
                Texture tex = mat.GetTexture(propName);
                if (tex != null) mat.SetTexture(propName, GetOrCreateCleanTexture(tex));
            }
        }
    }
    /// <summary>
    /// Gwarantuje unikalność binarną tekstur. Realizuje fizyczne kopiowanie plików graficznych 
    /// (PNG, TGA, JPG) do nowej lokalizacji.
    /// </summary>
    /// <param name="original">Surowa tekstura przypisana do oryginalnego materiału.</param>
    /// <returns>Referencja do nowej tekstury w folderze docelowym.</returns>
    /// <remarks>
    /// W odróżnieniu od materiałów, tekstury są kopiowane za pomocą <see cref="AssetDatabase.CopyAsset"/>. 
    /// Pozwala to na zachowanie specyficznych ustawień importu (Import Settings), takich jak 
    /// kompresja, generowanie mipmap czy ustawienia sRGB/Normal Map.
    /// </remarks>
    private Texture GetOrCreateCleanTexture(Texture original)
    {
        string oldPath = AssetDatabase.GetAssetPath(original);
        if (!oldPath.StartsWith(sourcePath)) return original;

        string newPath = oldPath.Replace(sourcePath, destinationPath);
        Texture existingTex = AssetDatabase.LoadAssetAtPath<Texture>(newPath);
        if (existingTex != null) return existingTex;

        Directory.CreateDirectory(Path.GetDirectoryName(newPath));
        AssetDatabase.CopyAsset(oldPath, newPath);
        return AssetDatabase.LoadAssetAtPath<Texture>(newPath);
    }
    /// <summary>
    /// Inicjalizuje przestrzeń roboczą dla procesu migracji (Filesystem Setup).
    /// </summary>
    /// <param name="path">Relatywna ścieżka do folderu (np. Assets/AssetyPrzerobione).</param>
    /// <remarks>
    /// Metoda ma charakter destrukcyjny: jeśli folder docelowy istnieje, zostaje on usunięty wraz 
    /// z całą zawartością, aby zapobiec konfliktom nazw i "zombie-assetom" z poprzednich przebiegów. 
    /// Następnie tworzy strukturę od zera i odświeża bazę danych <see cref="AssetDatabase"/>.
    /// </remarks>
    private void PrepareFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) AssetDatabase.DeleteAsset(path);
        string parent = Path.GetDirectoryName(path);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        AssetDatabase.Refresh();
    }
}