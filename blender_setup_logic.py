import bpy
import sys
import os

"""
<summary>
Główna procedura rekonstrukcji modelu i automatyzacji Shadera w środowisku Blendera.
Realizuje pełny cykl konwersji od surowej geometrii FBX/OBJ do gotowego kontenera .blend.
</summary>

<remarks>
<b>Proces składa się z czterech faz:</b>
1. <b>Inicjalizacja Środowiska:</b> Czyszczenie sceny i import geometrii z wymuszeniem globalnej skali 1.0.
2. <b>Normalizacja Transformacji:</b> Wykonanie operacji 'Transform Apply' na skali, co niweluje problem 
   dziesięciokrotnego powiększenia obiektów po powrocie do Unity (naprawa błędu skali 0.01).
3. <b>Analiza Batchy:</b> Identyfikacja obiektów w hierarchii na podstawie nazw przysłanych z Unity 
   i mapowanie slotów materiałowych.
4. <b>Rekonstrukcja Node-Link:</b> Budowa sieci węzłów Principled BSDF, podpięcie tekstur Albedo/Normal 
   oraz konfiguracja przestrzeni barwnej (Color Space) dla map normalnych.
</remarks>

<param name="temp_file_path">Ścieżka do pliku tekstowego wygenerowanego przez BlenderBridge.cs, zawierająca instrukcje wsadowe.</param>
"""

def setup_blend():
    # Pobranie argumentów i parsowanie ścieżek
    # ...
    
    # Przetwarzanie obiektów (Loop przez Batches)
    # 1. Znajdź obiekt po nazwie (startswith)
    # 2. Usuń/nadpisz sloty materiałowe
    # 3. Zbuduj sieć nodów dla każdego materiału
    # 4. Zmapuj tekstury i ustaw przestrzeń kolorów (Non-Color dla Normali)
    
    argv = sys.argv
    # Odbieramy tylko ścieżkę do pliku tekstowego z danymi
    temp_file_path = argv[argv.index("--") + 1]

    # Wczytywanie i parsowanie danych z pliku tymczasowego
    with open(temp_file_path, 'r') as f:
        lines = f.read().splitlines()

    mesh_path = lines[0]     # Oryginalny FBX
    output_path = lines[1]   # Docelowy .blend
    batches = lines[2:]      # Lista: "ObjName:MatName|A|N;MatName2|A|N"

    # --- LOGIKA OTWIERANIA LUB IMPORTU ---
    if os.path.exists(output_path):
        # Jeśli plik .blend już istnieje, OTWIERAMY GO (zachowując wcześniejszą pracę)
        bpy.ops.wm.open_mainfile(filepath=output_path)
    else:
        # Jeśli nie istnieje, czyścimy scenę i importujemy oryginalny plik FBX
        bpy.ops.wm.read_factory_settings(use_empty=True)
        ext = os.path.splitext(mesh_path)[1].lower()
        if ext == '.fbx':
            bpy.ops.import_scene.fbx(filepath=mesh_path)
        elif ext == '.obj':
            bpy.ops.import_scene.obj(filepath=mesh_path)

        # KLUCZOWY MOMENT: Resetujemy skalę obiektów do 1.0 (Apply Scale)
        # Robimy to dla wszystkich zaimportowanych obiektów, aby ich wymiary stały się ich "nową bazą"
        for obj in bpy.context.selected_objects:
            if obj.type == 'MESH':
                bpy.context.view_layer.objects.active = obj
                # Aplikujemy skalę (Scale -> 1.0, ale rozmiar w metrach zostaje bez zmian)
                bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
            
        # Zmiana nazwy Głównego Roota (opcjonalne, dla porządku)
        #if bpy.context.selected_objects:
        #    root = bpy.context.selected_objects[0]
        #    if not root.parent:
        #        root.name = os.path.splitext(os.path.basename(output_path))[0]

    # Zbierz wszystkie meshe dostępne aktualnie w Blenderze
    all_mesh_objects = [o for o in bpy.data.objects if o.type == 'MESH']

    # --- PRZETWARZANIE OBIEKTÓW ---
    for batch in batches:
        if not batch or ":" not in batch: continue
        
        obj_name, mats_str = batch.split(':')
        
        # Szukamy obiektu po nazwie. Używamy startswith, bo Blender czasem 
        # ucina bardzo długie nazwy lub dodaje numery przy kolizjach importu.
        target_obj = next((o for o in all_mesh_objects if o.name.startswith(obj_name)), None)
        
        if not target_obj:
            continue # Obiekt nie istnieje w tym pliku, pomijamy

        # Czyścimy sloty materiałowe WYŁĄCZNIE dla tego konkretnego obiektu
        #target_obj.data.materials.clear()

        mat_entries = mats_str.split(';')
        created_materials = []

        for entry in mat_entries:
            if not entry: continue
            
            parts = entry.split('|')
            if len(parts) < 3: continue
            
            mat_name, albedo_path, normal_path = parts

            # Sprawdzamy czy materiał już istnieje w pliku (żeby nie tworzyć klonów .001)
            if mat_name in bpy.data.materials:
                mat = bpy.data.materials[mat_name]
                mat.node_tree.nodes.clear() # Resetujemy istniejący materiał
            else:
                mat = bpy.data.materials.new(name=mat_name)
                mat.use_nodes = True
                mat.node_tree.nodes.clear()

            nodes = mat.node_tree.nodes
            links = mat.node_tree.links

            # Setup Nodes dla Blendera 4.0 / 5.0
            node_out = nodes.new(type='ShaderNodeOutputMaterial')
            node_out.location = (400, 0)
            bsdf = nodes.new(type='ShaderNodeBsdfPrincipled')
            bsdf.location = (0, 0)
            links.new(bsdf.outputs['BSDF'], node_out.inputs['Surface'])

            def add_tex(path, input_name, is_normal=False):
                path = path.strip().strip('"')
                if path != "NONE" and os.path.exists(path):
                    tex = nodes.new('ShaderNodeTexImage')
                    tex.image = bpy.data.images.load(path)
                    if is_normal:
                        tex.image.colorspace_settings.name = 'Non-Color'
                        n_map = nodes.new('ShaderNodeNormalMap')
                        links.new(tex.outputs['Color'], n_map.inputs['Color'])
                        links.new(n_map.outputs['Normal'], bsdf.inputs['Normal'])
                    else:
                        links.new(tex.outputs['Color'], bsdf.inputs[input_name])

            add_tex(albedo_path, 'Base Color')
            add_tex(normal_path, 'Normal', True)

            # Przypisujemy do slotu obiektu
            target_obj.data.materials.append(mat)
            created_materials.append(mat)

        for i, new_mat in enumerate(created_materials):
            if i < len(target_obj.data.materials):
                # Jeśli slot istnieje, podmień materiał
                target_obj.data.materials[i] = new_mat
            else:
                # Jeśli Unity ma więcej materiałów niż Blender wykrył w meshu, dodaj nowy slot
                target_obj.data.materials.append(new_mat)

    # Zapis pliku
    clean_output = output_path.strip().strip('"')
    
    # Tworzenie folderów jeśli nie istnieją
    os.makedirs(os.path.dirname(clean_output), exist_ok=True)
    
    bpy.ops.wm.save_as_mainfile(filepath=clean_output)

if __name__ == "__main__":
    setup_blend()