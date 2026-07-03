# AssetFlow: Unity-Blender Pipeline

AssetFlow to zestaw narzędzi do automatyzacji workflow'u między Unity a Blenderem. Pipeline rozwiązuje problem "zaśmieconych" assetów importowanych (np. z Asset Store), przeprowadzając je przez proces głębokiej rekonstrukcji, konwersji do `.blend` i finalnej podmiany w prefabach.

## Architektura systemu

System składa się z czterech głównych komponentów:

1. **`TextureAutomator` (Krok 1: Rekonstrukcja)**
   - Czyści folder docelowy.
   - Tworzy "czyste" kopie prefabów, izolując je od oryginalnych materiałów/tekstur.
   - Rekurencyjnie kopiuje hierarchię obiektów i strukturę renderowania.
   - Odpowiada za izolację tekstur (Deep Isolation).

2. **`BlenderBridge` (Krok 3: Automatyzacja)**
   - Główny orchestrator.
   - Analizuje prefaby w Unity.
   - Generuje instrukcje dla Blendera (`blender_args.txt`).
   - Uruchamia Blender w trybie `headless` (`-b`) i podmienia referencje meshy w Unity na gotowe pliki `.blend`.

3. **`blender_setup_logic.py` (Silnik Blendera)**
   - Skrypt wykonywany wewnątrz środowiska Blendera.
   - Resetuje skalę (Apply Scale = 1.0) – rozwiązuje problem z "dziesięciokrotnym powiększeniem" w Unity.
   - Rekonstruuje sieć nodów (Principled BSDF) dla każdego materiału.
   - Mapuje tekstury (Albedo/Normal) i ustawia przestrzeń kolorów (Non-Color dla normali).

4. **`ImageCopierWindow` (Narzędzie Ratunkowe)**
   - "Brute-force" do kopiowania obrazów między partycjami.
   - Używane, gdy pipeline automatyczny wymaga ręcznej interwencji w tekstury.

## Wymagania
* Unity Editor (testowane na wersjach 2021+).
* Blender zainstalowany w systemie (wymagana konfiguracja ścieżki w skryptach).
* Systemy: Linux / Windows.

## Workflow (Jak tego używać)

### 1. Przygotowanie ("Głęboka Rekonstrukcja")
Otwórz `Tools -> 1. Głęboka Rekonstrukcja Assetów`.
Wskaż folder źródłowy (z `Assets/AssetyImportowane_Objekty`) i docelowy. Uruchom proces.
*Uwaga: To usunie wszystko w folderze docelowym przed startem!*

### 2. Kopiowanie (Opcjonalnie)
Jeśli brakuje tekstur, użyj `Tools -> 2. Kopiarka Obrazów`. Działa na twardych ścieżkach systemowych.

### 3. Konwersja (Most do Blendera)
Otwórz `Tools -> 3. Pełna Automatyzacja: Multi-Mesh FBX`.
Upewnij się, że ścieżka do `blender.exe` (lub `blender` w Linux) jest poprawna w ustawieniach okna. Uruchom proces "Smart Batching".

## ⚠️ Ważne ostrzeżenia (Dług technologiczny)

- **Hardcoded Paths:** Skrypty (`ImageCopierWindow`, `BlenderBridge`) posiadają wpisane na sztywno ścieżki systemowe do folderów. Przed pierwszym uruchomieniem sprawdź zmienne `sourceFolder`, `destinationPath` oraz `blenderPath`.
- **Destrukcyjność:** `TextureAutomator` czyści cały folder docelowy. Upewnij się, że nie trzymasz tam żadnych plików, których nie chcesz stracić.
- **Skalowalność:** Przy bardzo dużej liczbie obiektów Blender może zjeść pamięć RAM. Proces jest uruchamiany w tle (tryb `headless`).
- **Narzędzie "Brudne":** Pipeline działa, ale wymaga uwagi przy zmianie lokalizacji projektu. Nie przenoś projektu bez aktualizacji ścieżek w kodzie C#.

## Licencja
Projekt "As-is". Używasz na własną odpowiedzialność.
