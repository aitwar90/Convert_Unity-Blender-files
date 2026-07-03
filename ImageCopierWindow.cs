using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;

/// <summary>
/// Narzędzie pomocnicze dedykowane dla systemu Windows, optymalizujące proces kopiowania dużych zbiorów 
/// tekstur i danych binarnych między partycjami projektu.
/// <remarks>
/// Wykorzystuje systemowe mechanizmy IO do zapewnienia spójności ścieżek bez blokowania wątku głównego Unity 
/// podczas operacji na ciężkich assetach graficznych.
/// </remarks>
/// </summary>
public class ImageCopierWindow : EditorWindow
{
    /// <summary>
    /// Ścieżka źródłowa (magazyn), z której pobierane są surowe pliki graficzne.
    /// </summary>
    private string sourceFolder = "/media/aitwarcl/b6fec136-6fdf-43ec-aa0e-0c5f6a0afa37/Projekty Unity/AssetyPrzerobione";
    /// <summary>
    /// Ścieżka docelowa wewnątrz projektu Unity (Assets), gdzie obrazy mają zostać zaktualizowane.
    /// </summary>
    private string destinationFolder = "/media/aitwarcl/b6fec136-6fdf-43ec-aa0e-0c5f6a0afa37/Projekty Unity/DeathTrain/DeathTrain/Assets/AssetyPrzerobione";

    /// <summary>
    /// Rejestr obsługiwanych formatów graficznych. Pliki z rozszerzeniami spoza tej listy 
    /// (w tym pliki .meta) są całkowicie ignorowane podczas procesu kopiowania.
    /// </summary>
    private readonly string[] imageExtensions = { 
        ".png", ".jpg", ".jpeg", ".tiff", ".tif", ".bmp", ".tga", ".psd", ".exr", ".hdr", ".gif", ".webp" 
    };

    /// <summary>
    /// Inicjalizuje i wyświetla okno narzędzia w edytorze Unity.
    /// </summary>
    [MenuItem("Tools/2. Kopiarka Obrazów (Brute-Force)")]
    public static void ShowWindow() => GetWindow<ImageCopierWindow>("2. Kopiarka Obrazów");

    /// <summary>
    /// Rysuje interfejs użytkownika, umożliwiając konfigurację ścieżek oraz walidację operacji nadpisywania.
    /// </summary>
    private void OnGUI()
    {
        GUILayout.Label("Masowe Kopiowanie Obrazów", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Skrypt ignoruje pliki .meta i kopiuje wyłącznie pliki graficzne, zachowując strukturę folderów i nadpisując istniejące pliki w celu.", MessageType.Info);

        sourceFolder = EditorGUILayout.TextField("Folder Źródłowy:", sourceFolder);
        destinationFolder = EditorGUILayout.TextField("Folder Docelowy:", destinationFolder);

        GUILayout.Space(10);

        if (GUILayout.Button("Kopiuj i Nadpisz Obrazy", GUILayout.Height(30)))
        {
            if (EditorUtility.DisplayDialog("Uwaga", "Czy na pewno chcesz przekopiować i nadpisać obrazy? Ta operacja zignoruje pliki .meta.", "Lecimy!", "Anuluj"))
            {
                RunCopy();
            }
        }
    }
    /// <summary>
    /// Główna procedura kopiowania. Wykonuje iteracyjne przeszukiwanie katalogów (Recursive Search), 
    /// filtruje pliki według rozszerzeń i mapuje relatywną strukturę folderów ze źródła do celu.
    /// </summary>
    /// <remarks>
    /// Wykorzystuje <see cref="EditorUtility.DisplayProgressBar"/> do monitorowania postępu przy dużych zbiorach danych 
    /// oraz <see cref="AssetDatabase.Refresh"/> na końcu operacji, aby wymusić na Unity reimport nowych plików.
    /// </remarks>
    private void RunCopy()
    {
        // Standaryzacja ścieżek (żeby uniknąć problemów ze slashami w systemach Windows/Mac)
        string normSource = Path.GetFullPath(sourceFolder).Replace('\\', '/');
        string normDest = Path.GetFullPath(destinationFolder).Replace('\\', '/');

        if (!Directory.Exists(normSource))
        {
            Debug.LogError($"[Kopiarka] Folder źródłowy nie istnieje: {normSource}");
            return;
        }

        // Pobierz wszystkie pliki ze źródła (razem z podfolderami)
        string[] allFiles = Directory.GetFiles(normSource, "*.*", SearchOption.AllDirectories);
        
        int copiedCount = 0;
        int totalFiles = allFiles.Length;

        for (int i = 0; i < totalFiles; i++)
        {
            string filePath = allFiles[i].Replace('\\', '/');
            string extension = Path.GetExtension(filePath).ToLower();

            // Pasek postępu, żeby Unity nie wyglądało na "zawieszone"
            EditorUtility.DisplayProgressBar("Kopiowanie Obrazów", $"Przetwarzanie: {Path.GetFileName(filePath)}", (float)i / totalFiles);

            // Ignoruj pliki .meta i upewnij się, że to format obrazu
            if (extension == ".meta" || !imageExtensions.Contains(extension))
                continue;

            // Oblicz nową ścieżkę (zastępując część źródłową docelową)
            string relativePath = filePath.Substring(normSource.Length).TrimStart('/');
            string targetPath = Path.Combine(normDest, relativePath).Replace('\\', '/');

            // Upewnij się, że folder docelowy dla tego konkretnego pliku istnieje
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath));

            // Skopiuj z nadpisaniem (true)
            File.Copy(filePath, targetPath, true);
            copiedCount++;
        }

        EditorUtility.ClearProgressBar();
        
        // Odśwież bazę assetów, żeby Unity "zobaczyło" skopiowane pliki i wygenerowało im nowe .meta
        AssetDatabase.Refresh();

        Debug.Log($"<color=green>Zakończono sukcesem! Przekopiowano i nadpisano <b>{copiedCount}</b> obrazów.</color>");
    }
}