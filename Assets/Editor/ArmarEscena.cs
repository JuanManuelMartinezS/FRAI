#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEditor.XR.ARSubsystems;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace GuiaFrailejon.EditorTools
{
    /// <summary>
    /// Arma TODA la escena del guia con un clic:
    /// menu de Unity  ->  Guia Frailejon  ->  Armar escena completa
    ///
    /// Antes de usarlo:
    ///  - Paquetes instalados: AR Foundation, Google ARCore XR Plugin, glTFast (si el modelo es .glb).
    ///  - TMP Essentials importado (Window > TextMeshPro > Import TMP Essential Resources).
    ///  - (Opcional) su modelo del frailejon en Assets/Modelos  (.glb, .gltf, .fbx u .obj).
    ///  - (Opcional) sus 4 imagenes de letreros en Assets/Marcadores. Si no hay, usa Assets/MarcadoresEjemplo.
    /// </summary>
    public static class ArmarEscena
    {
        const string RutaEscena = "Assets/Scenes/GuiaFrailejon.unity";
        const string CarpetaPrefabs = "Assets/Prefabs";
        const string RutaPrefabFrailejon = CarpetaPrefabs + "/FrailejonGuia.prefab";
        const string RutaPrefabChip = CarpetaPrefabs + "/Chip.prefab";
        const string RutaMaterialFondo = "Assets/Materiales/FondoSinAR.mat";
        const float AnchoMarcadorMetros = 0.25f;   // ancho real impreso del letrero

        static string rutaLibreria;   // se recarga por ruta: ARCore reimporta la libreria y la referencia en memoria queda invalida

        static System.Collections.Generic.List<string> rutasImagenes = new System.Collections.Generic.List<string>();

        [MenuItem("Guia Frailejon/Armar escena completa", priority = 0)]
        public static void Armar()
        {
            if (Resources.Load<TMP_Settings>("TMP Settings") == null)
            {
                EditorUtility.DisplayDialog("Falta TextMeshPro",
                    "Primero importen TMP Essentials:\nWindow > TextMeshPro > Import TMP Essential Resources\n\nLuego vuelvan a ejecutar este menu.", "OK");
                return;
            }
            if (!EditorUtility.DisplayDialog("Armar escena del Guia Frailejon",
                "Se va a crear una escena NUEVA (Assets/Scenes/GuiaFrailejon.unity) con todo armado y conectado.\n\n" +
                "Su escena actual no se borra. Si ya existe GuiaFrailejon.unity, se reemplaza.\n\n¿Continuar?", "Si, armar", "Cancelar"))
                return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            try
            {
                EditorUtility.DisplayProgressBar("Guia Frailejon", "Preparando carpetas y prefabs...", 0.1f);
                CrearCarpeta(CarpetaPrefabs);
                CrearCarpeta("Assets/Scenes");
                CrearCarpeta("Assets/Materiales");

                GameObject prefabFrailejon = CrearPrefabFrailejon();
                Button prefabChip = CrearPrefabChip();

                EditorUtility.DisplayProgressBar("Guia Frailejon", "Creando marcadores...", 0.3f);
                XRReferenceImageLibrary libreria = CrearLibreriaMarcadores();
                var fotos = rutasImagenes.Select(AssetDatabase.LoadAssetAtPath<Texture2D>).ToArray();

                EditorUtility.DisplayProgressBar("Guia Frailejon", "Armando la escena...", 0.5f);
                var escena = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

                var luz = new GameObject("Directional Light").AddComponent<Light>();
                luz.type = LightType.Directional;
                luz.transform.rotation = Quaternion.Euler(50, -30, 0);

                // ---- Sistema (cerebro, GPS) ----
                var sistema = new GameObject("Sistema");
                var kb = sistema.AddComponent<KnowledgeBase>();
                var cerebro = sistema.AddComponent<GuideBrain>();
                cerebro.kb = kb;
                var updater = sistema.AddComponent<ContentUpdater>();
                updater.kb = kb;
                var disponibilidad = sistema.AddComponent<ARAvailability>();
                var geo = sistema.AddComponent<GeoLocalizador>();
                geo.kb = kb;

                // ---- AR ----
                var raizAR = new GameObject("RaizAR");
                bool arOk = CrearObjetosAR(raizAR.transform, libreria, prefabFrailejon, kb, out FrailejonSpawner spawner, out FraiAcompanante acompanante);

                // ---- Modo sin AR ----
                var raizSinAR = new GameObject("RaizSinAR");
                var modoSinAR = CrearModoSinAR(raizSinAR, prefabFrailejon, kb, fotos);
                disponibilidad.raizAR = raizAR;
                disponibilidad.raizSinAR = raizSinAR;

                // ---- Interfaz ----
                EditorUtility.DisplayProgressBar("Guia Frailejon", "Creando la interfaz...", 0.7f);
                CrearEventSystem();
                CrearInterfaz(cerebro, kb, spawner, acompanante, disponibilidad, modoSinAR, geo, prefabChip);

                // ---- Guardar ----
                EditorUtility.DisplayProgressBar("Guia Frailejon", "Guardando...", 0.9f);
                EditorSceneManager.SaveScene(escena, RutaEscena);
                AgregarEscenaAlBuild(RutaEscena);
                bool reiniciar = ConfigurarPlayer();
                AssetDatabase.SaveAssets();

                EditorUtility.ClearProgressBar();
                string msg = "¡Listo! Escena creada en " + RutaEscena + ".\n\n" +
                             "Si es la primera vez: marquen Google ARCore en XR Plug-in Management (pestaña Android) y denle Fix All en Project Validation.\n\n" +
                             "Luego: Play para probar en el computador, o Build para el celular.";
                if (reiniciar)
                    msg += "\n\nIMPORTANTE: se cambió 'Active Input Handling' a 'Both' (necesario para GPS e inclinación). CIERREN Y VUELVAN A ABRIR UNITY antes de compilar.";
                if (!arOk)
                    msg += "\n\nAVISO: no se pudo crear el XR Origin automaticamente. Revisen la Console.";
                EditorUtility.DisplayDialog("Guia Frailejon", msg, "OK");

                // Si la IA o la voz ya estaban activadas, se vuelven a conectar en la escena nueva.
                var simbolos = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Android).Split(';');
                if (simbolos.Contains("GUIA_LLMUNITY")) ActivarIA.Activar();
                if (simbolos.Contains("GUIA_WHISPER")) ActivarVoz.Activar();
            }
            catch (Exception e)
            {
                EditorUtility.ClearProgressBar();
                Debug.LogException(e);
                EditorUtility.DisplayDialog("Guia Frailejon", "Algo fallo: " + e.Message + "\n\nCopien el error rojo de la Console y pidan ayuda.", "OK");
            }
        }

        // =====================================================================
        // Prefab del frailejon
        // =====================================================================
        static GameObject CrearPrefabFrailejon()
        {
            string rutaModelo = BuscarModelo();
            var raiz = new GameObject("FrailejonGuia");
            GameObject cuerpo;

            if (rutaModelo != null)
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(rutaModelo);
                if (asset == null) throw new Exception("No se pudo cargar el modelo " + rutaModelo +
                    ". Si es .glb, revisen que glTFast este instalado.");
                cuerpo = (GameObject)PrefabUtility.InstantiatePrefab(asset);
                cuerpo.name = "Modelo";
                cuerpo.transform.SetParent(raiz.transform, false);
                NormalizarTamano(cuerpo.transform, 1.0f);
                Debug.Log("[ArmarEscena] Usando su modelo: " + rutaModelo);
            }
            else
            {
                cuerpo = CrearFrailejonProvisional(raiz.transform);
                Debug.Log("[ArmarEscena] No hay modelo en Assets/Modelos: se usa un frailejon provisional.");
            }

            var anim = raiz.AddComponent<FrailejonAnimator>();
            anim.roseta = cuerpo.transform;

            var prefab = PrefabUtility.SaveAsPrefabAsset(raiz, RutaPrefabFrailejon);
            UnityEngine.Object.DestroyImmediate(raiz);
            return prefab;
        }

        static string BuscarModelo()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Modelos")) return null;
            string[] ext = { ".glb", ".gltf", ".fbx", ".obj" };
            return AssetDatabase.FindAssets("", new[] { "Assets/Modelos" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => ext.Contains(Path.GetExtension(p).ToLowerInvariant()))
                .OrderBy(p => Array.IndexOf(ext, Path.GetExtension(p).ToLowerInvariant()))
                .FirstOrDefault();
        }

        /// <summary>Deja el modelo de 'altura' metros, centrado y con la base en el piso.</summary>
        static void NormalizarTamano(Transform t, float altura)
        {
            var rs = t.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return;
            Bounds b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            float maxLado = Mathf.Max(b.size.y, 0.0001f);
            t.localScale *= altura / maxLado;

            b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            t.position -= new Vector3(b.center.x, b.min.y, b.center.z);
        }

        static GameObject CrearFrailejonProvisional(Transform padre)
        {
            CrearCarpeta("Assets/Materiales");
            var verde = CrearMaterial("Assets/Materiales/FrailejonHojas.mat", new Color(0.55f, 0.65f, 0.45f));
            var cafe = CrearMaterial("Assets/Materiales/FrailejonTallo.mat", new Color(0.35f, 0.25f, 0.15f));

            var cuerpo = new GameObject("Cuerpo");
            cuerpo.transform.SetParent(padre, false);

            var tallo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            tallo.name = "Tallo";
            tallo.transform.SetParent(cuerpo.transform, false);
            tallo.transform.localPosition = new Vector3(0, 0.35f, 0);
            tallo.transform.localScale = new Vector3(0.16f, 0.35f, 0.16f);
            tallo.GetComponent<Renderer>().sharedMaterial = cafe;

            var roseta = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            roseta.name = "Roseta";
            roseta.transform.SetParent(cuerpo.transform, false);
            roseta.transform.localPosition = new Vector3(0, 0.8f, 0);
            roseta.transform.localScale = new Vector3(0.6f, 0.4f, 0.6f);
            roseta.GetComponent<Renderer>().sharedMaterial = verde;

            foreach (var col in cuerpo.GetComponentsInChildren<Collider>()) UnityEngine.Object.DestroyImmediate(col);
            return cuerpo;
        }

        static Material CrearMaterial(string ruta, Color color)
        {
            var existente = AssetDatabase.LoadAssetAtPath<Material>(ruta);
            if (existente != null) return existente;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(shader) { color = color };
            AssetDatabase.CreateAsset(m, ruta);
            return m;
        }

        // =====================================================================
        // Marcadores
        // =====================================================================
        static XRReferenceImageLibrary CrearLibreriaMarcadores()
        {
            string carpeta = TieneImagenes("Assets/Marcadores") ? "Assets/Marcadores" : "Assets/MarcadoresEjemplo";
            var imagenes = AssetDatabase.FindAssets("t:Texture2D", new[] { carpeta })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => Path.GetDirectoryName(p).Replace('\\', '/') == carpeta)
                .OrderBy(p => p)
                .Take(4)
                .ToList();
            if (imagenes.Count == 0) throw new Exception("No hay imagenes de marcadores en Assets/Marcadores ni en Assets/MarcadoresEjemplo.");

            rutasImagenes = imagenes;
            string rutaLib = carpeta + "/LibreriaEstaciones.asset";
            AssetDatabase.DeleteAsset(rutaLib);
            var lib = ScriptableObject.CreateInstance<XRReferenceImageLibrary>();
            AssetDatabase.CreateAsset(lib, rutaLib);

            for (int i = 0; i < imagenes.Count; i++)
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(imagenes[i]);
                if (importer != null && !importer.isReadable) { importer.isReadable = true; importer.SaveAndReimport(); }

                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(imagenes[i]);
                lib.Add();
                lib.SetTexture(i, tex, true);
                lib.SetName(i, "estacion_" + (i + 1));
                lib.SetSpecifySize(i, true);
                lib.SetSize(i, new Vector2(AnchoMarcadorMetros, AnchoMarcadorMetros * tex.height / (float)tex.width));
                Debug.Log($"[ArmarEscena] estacion_{i + 1} = {imagenes[i]}");
            }
            if (imagenes.Count < 4) Debug.LogWarning($"[ArmarEscena] Solo hay {imagenes.Count} imagenes de marcador (se esperaban 4).");

            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(rutaLib, ImportAssetOptions.ForceUpdate);
            rutaLibreria = rutaLib;
            return AssetDatabase.LoadAssetAtPath<XRReferenceImageLibrary>(rutaLib);
        }

        static bool TieneImagenes(string carpeta) =>
            AssetDatabase.IsValidFolder(carpeta) && AssetDatabase.FindAssets("t:Texture2D", new[] { carpeta }).Length > 0;

        // =====================================================================
        // AR Session + XR Origin
        // =====================================================================
        static bool CrearObjetosAR(Transform raizAR, XRReferenceImageLibrary lib, GameObject prefab, KnowledgeBase kb,
                                   out FrailejonSpawner spawner, out FraiAcompanante acompanante)
        {
            spawner = null;
            acompanante = null;

            var sesion = new GameObject("AR Session", typeof(ARSession), typeof(ARInputManager));
            sesion.transform.SetParent(raizAR);

            // Se usa el menu oficial de AR Foundation, que configura camara y seguimiento correctamente.
            Selection.activeGameObject = raizAR.gameObject;
            bool creado = EditorApplication.ExecuteMenuItem("GameObject/XR/XR Origin (Mobile AR)");
            var origen = UnityEngine.Object.FindObjectsByType<XROrigin>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
            if (!creado || origen == null)
            {
                Debug.LogError("[ArmarEscena] No se pudo crear el XR Origin. Haganlo a mano: clic derecho sobre RaizAR > XR > XR Origin (Mobile AR); " +
                               "luego agreguen 'AR Tracked Image Manager' y 'Frailejon Spawner' (README, Partes 6.4 y 6.9).");
                return false;
            }
            origen.transform.SetParent(raizAR, true);

            // Recargar la libreria desde el disco justo antes de asignarla.
            if (!string.IsNullOrEmpty(rutaLibreria))
                lib = AssetDatabase.LoadAssetAtPath<XRReferenceImageLibrary>(rutaLibreria);
            if (lib == null) Debug.LogError("[ArmarEscena] No se pudo cargar la libreria en " + rutaLibreria);

            var gestor = origen.gameObject.AddComponent<ARTrackedImageManager>();
            var so = new SerializedObject(gestor);
            var pLib = so.FindProperty("m_SerializedLibrary");
            var pMax = so.FindProperty("m_MaxNumberOfMovingImages");
            if (pLib != null) pLib.objectReferenceValue = lib; else gestor.referenceLibrary = lib;
            if (pMax != null) pMax.intValue = 1;
            so.ApplyModifiedPropertiesWithoutUndo();

            if (gestor.referenceLibrary == null)
                Debug.LogError("[ArmarEscena] El ARTrackedImageManager quedo SIN libreria. Arrastren LibreriaEstaciones a su campo 'Serialized Library'.");
            else
                Debug.Log("[ArmarEscena] Libreria de letreros asignada: " + lib.name + " (" + lib.count + " imagenes)");

            spawner = origen.gameObject.AddComponent<FrailejonSpawner>();
            spawner.libreria = lib;
            spawner.prefabFrailejon = prefab;
            spawner.kb = kb;

            // Deteccion del piso (para que Frai camine al lado del visitante).
            var planos = origen.gameObject.AddComponent<ARPlaneManager>();
            planos.requestedDetectionMode = PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical;   // piso + paredes/muebles
            origen.gameObject.AddComponent<ARRaycastManager>();
            acompanante = origen.gameObject.AddComponent<FraiAcompanante>();
            acompanante.prefabFrailejon = prefab;
            spawner.acompanante = acompanante;
            return true;
        }

        // =====================================================================
        // UI
        // =====================================================================
        // ---- Paleta moderna: azul noche translucido ("glass") + acentos azul / ambar ----
        static readonly Color Cristal = new Color(0.04f, 0.12f, 0.27f, 0.80f);
        static readonly Color CristalClaro = new Color(1f, 1f, 1f, 0.14f);
        static readonly Color Azul = new Color(0.13f, 0.50f, 0.98f);
        static readonly Color Ambar = new Color(1f, 0.80f, 0.15f);
        static readonly Color Turquesa = new Color(0.10f, 0.62f, 0.50f);
        static readonly Color TextoSuave = new Color(0.78f, 0.88f, 1f);

        /// <summary>Rectangulo de esquinas redondeadas (9-slice) generado por codigo, sin depender de arte externo.</summary>
        static Sprite Redondeado()
        {
            const string ruta = "Assets/Materiales/UIRedondeado.png";
            if (!File.Exists(ruta))
            {
                const int N = 128; const float R = 40f;
                var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float px = x + 0.5f, py = y + 0.5f;
                        float cx = Mathf.Clamp(px, R, N - R), cy = Mathf.Clamp(py, R, N - R);
                        float d = Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
                        tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(R - d + 0.5f)));
                    }
                File.WriteAllBytes(ruta, tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(ruta, ImportAssetOptions.ForceUpdate);
                var imp = (TextureImporter)AssetImporter.GetAtPath(ruta);
                imp.textureType = TextureImporterType.Sprite;
                imp.spriteImportMode = SpriteImportMode.Single;
                imp.spriteBorder = new Vector4(44, 44, 44, 44);
                imp.mipmapEnabled = false;
                imp.alphaIsTransparency = true;
                imp.textureCompression = TextureImporterCompression.Uncompressed;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(ruta);
        }

        static TMP_DefaultControls.Resources RecursosUI() => new TMP_DefaultControls.Resources
        {
            standard = Redondeado(),
            background = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd"),
            inputField = Redondeado(),
            knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd"),
            checkmark = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd"),
            dropdown = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/DropdownArrow.psd"),
            mask = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UIMask.psd"),
        };

        static void CrearEventSystem()
        {
            var es = new GameObject("EventSystem", typeof(EventSystem));
            var tipoNuevo = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (tipoNuevo != null) es.AddComponent(tipoNuevo);
            else es.AddComponent<StandaloneInputModule>();
        }

        static Canvas CrearCanvas(string nombre, Transform padre, int orden)
        {
            var go = new GameObject(nombre, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            if (padre) go.transform.SetParent(padre, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = orden;
            var esc = go.GetComponent<CanvasScaler>();
            esc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            esc.referenceResolution = new Vector2(1080, 1920);
            esc.matchWidthOrHeight = 0f;   // ancho fijo de 1080: en celulares altos crece el alto, no se encima nada
            return canvas;
        }

        enum Ancla { ArribaCentro, ArribaIzq, ArribaDer, AbajoCentro, AbajoIzq, AbajoDer }

        static void Ubicar(GameObject go, Ancla a, float x, float y, float w, float h)
        {
            var rt = go.GetComponent<RectTransform>();
            if (rt == null) rt = go.AddComponent<RectTransform>();
            Vector2 p = a switch
            {
                Ancla.ArribaCentro => new Vector2(0.5f, 1), Ancla.ArribaIzq => new Vector2(0, 1), Ancla.ArribaDer => new Vector2(1, 1),
                Ancla.AbajoCentro => new Vector2(0.5f, 0), Ancla.AbajoIzq => new Vector2(0, 0), _ => new Vector2(1, 0),
            };
            rt.anchorMin = rt.anchorMax = rt.pivot = p;
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
        }

        static TMP_Text Texto(Transform padre, string nombre, string contenido, float tam, Ancla a, float x, float y, float w, float h,
                              TextAlignmentOptions alin = TextAlignmentOptions.Center)
        {
            var go = new GameObject(nombre, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(padre, false);
            Ubicar(go, a, x, y, w, h);
            var t = go.GetComponent<TextMeshProUGUI>();
            t.text = contenido;
            t.fontSize = tam;
            t.color = Color.white;
            t.alignment = alin;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }

        static Button Boton(Transform padre, string nombre, string texto, float tam, Color fondo, Color colorTexto)
        {
            var go = TMP_DefaultControls.CreateButton(RecursosUI());
            go.name = nombre;
            go.transform.SetParent(padre, false);
            go.GetComponent<Image>().color = fondo;
            var t = go.GetComponentInChildren<TMP_Text>();
            t.text = texto;
            t.fontSize = tam;
            t.color = colorTexto;
            return go.GetComponent<Button>();
        }

        static void Rect(GameObject go, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.pivot = pivot;
            rt.anchoredPosition = pos; rt.sizeDelta = size;
        }

        static GameObject Panel(string nombre, Transform padre, Color color)
        {
            var go = new GameObject(nombre, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(padre, false);
            var img = go.GetComponent<Image>();
            img.sprite = Redondeado();
            img.type = Image.Type.Sliced;
            img.color = color;
            return go;
        }

        static readonly Color Verde = new Color(0.20f, 0.45f, 0.25f);
        static readonly Color Rojo = new Color(0.86f, 0.18f, 0.20f);
        static readonly Color Oscuro = new Color(0.12f, 0.12f, 0.12f);

        static GuideUI CrearInterfaz(GuideBrain cerebro, KnowledgeBase kb, FrailejonSpawner spawner, FraiAcompanante acompanante,
                                     ARAvailability disponibilidad, ModoSinAR modoSinAR, GeoLocalizador geo, Button prefabChip)
        {
            var canvas = CrearCanvas("Canvas", null, 0);
            var t = canvas.transform;
            var spriteRedondo = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            var spriteCirculo = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");

            // ---------------- Barra superior ----------------
            // Tarjeta flotante de cristal azul con esquinas redondeadas.
            var barra = Panel("BarraSuperior", t, Cristal);
            Rect(barra, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -16), new Vector2(-32, 320));
            barra.GetComponent<Image>().raycastTarget = false;

            var marca = Texto(barra.transform, "TextoParque", "Parque Nacional\nLos Nevados", 27, Ancla.ArribaIzq, 28, -22, 330, 76, TextAlignmentOptions.Left);
            marca.fontStyle = FontStyles.Bold;
            marca.textWrappingMode = TextWrappingModes.NoWrap;

            // "Pastilla" con la estacion actual
            var pastilla = Panel("PastillaEstacion", barra.transform, CristalClaro);
            Ubicar(pastilla, Ancla.ArribaIzq, 372, -22, 440, 76);
            pastilla.GetComponent<Image>().raycastTarget = false;
            var punto = Panel("Punto", pastilla.transform, Ambar);
            punto.GetComponent<Image>().sprite = spriteCirculo;
            Ubicar(punto, Ancla.ArribaIzq, 22, -26, 24, 24);
            var estacion = Texto(pastilla.transform, "TextoEstacion", "Guía Frailejón", 32, Ancla.ArribaIzq, 58, 0, 370, 76, TextAlignmentOptions.Left);
            estacion.fontStyle = FontStyles.Bold;
            estacion.enableAutoSizing = true; estacion.fontSizeMin = 22; estacion.fontSizeMax = 32;
            var progreso = Texto(barra.transform, "TextoProgreso", "", 24, Ancla.ArribaIzq, 36, -108, 980, 36, TextAlignmentOptions.Left);
            progreso.color = TextoSuave;

            var emergencia = Boton(barra.transform, "BtnEmergencia", "SOS", 34, Rojo, Color.white);
            emergencia.GetComponentInChildren<TMP_Text>().fontStyle = FontStyles.Bold;
            Ubicar(emergencia.gameObject, Ancla.ArribaDer, -22, -22, 190, 76);

            var mapaGo = new GameObject("Mapa", typeof(RectTransform));
            mapaGo.transform.SetParent(barra.transform, false);
            Rect(mapaGo, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -146), new Vector2(-40, 160));
            var mapa = mapaGo.AddComponent<MiniMapa>();
            mapa.kb = kb;
            mapa.geo = geo;
            mapa.circulo = spriteCirculo;

            // ---------------- Diagnostico de AR (texto amarillo) ----------------
            var dbg = Texto(t, "TextoDebug", "", 20, Ancla.ArribaCentro, 0, -346, 1040, 56);
            dbg.color = new Color(1f, 0.9f, 0.4f);
            if (spawner) spawner.textoDebug = dbg;

            // ---------------- Botones flotantes (cristal) ----------------
            var silenciar = Boton(t, "BtnSilenciar", "Silenciar", 26, Cristal, Color.white);
            Ubicar(silenciar.gameObject, Ancla.ArribaIzq, 28, -410, 220, 80);
            var acompanar = Boton(t, "BtnAcompanar", "Acompáñame", 28, Turquesa, Color.white);
            Ubicar(acompanar.gameObject, Ancla.ArribaDer, -28, -410, 300, 80);
            var venAqui = Boton(t, "BtnVenAqui", "Ven aquí", 26, Cristal, Color.white);
            Ubicar(venAqui.gameObject, Ancla.ArribaDer, -340, -410, 200, 80);

            // Indicador de borde: "Frai >" cuando Frai queda fuera de la pantalla
            var indicador = Panel("IndicadorFrai", t, new Color(0.13f, 0.50f, 0.98f, 0.92f));
            indicador.GetComponent<Image>().raycastTarget = false;
            var irt = (RectTransform)indicador.transform;
            irt.sizeDelta = new Vector2(190, 70);
            var textoInd = Texto(indicador.transform, "Texto", "Frai  >", 28, Ancla.ArribaCentro, 0, 0, 190, 70);
            textoInd.rectTransform.anchorMin = Vector2.zero; textoInd.rectTransform.anchorMax = Vector2.one;
            textoInd.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            textoInd.rectTransform.sizeDelta = Vector2.zero; textoInd.rectTransform.anchoredPosition = Vector2.zero;
            if (acompanante) { acompanante.indicador = irt; acompanante.textoIndicador = textoInd; }
            indicador.SetActive(false);

            // ---------------- Panel inferior ----------------
            var panel = Panel("PanelInferior", t, Cristal);
            Rect(panel, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 16), new Vector2(-32, 820));

            var plegar = Boton(panel.transform, "BtnPlegar", "Ocultar chat", 22, CristalClaro, Color.white);
            Ubicar(plegar.gameObject, Ancla.ArribaCentro, 0, -12, 240, 50);

            // Conversacion (ScrollRect vertical)
            var chatGo = new GameObject("Chat", typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
            chatGo.transform.SetParent(panel.transform, false);
            var chatRt = (RectTransform)chatGo.transform;
            chatRt.anchorMin = Vector2.zero; chatRt.anchorMax = Vector2.one;
            chatRt.offsetMin = new Vector2(16, 310); chatRt.offsetMax = new Vector2(-16, -70);
            chatGo.GetComponent<Image>().color = new Color(0, 0, 0, 0);   // invisible, pero permite arrastrar

            var contGo = new GameObject("Contenido", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contGo.transform.SetParent(chatGo.transform, false);
            var contRt = (RectTransform)contGo.transform;
            contRt.anchorMin = new Vector2(0, 1); contRt.anchorMax = new Vector2(1, 1); contRt.pivot = new Vector2(0.5f, 1);
            contRt.anchoredPosition = Vector2.zero; contRt.sizeDelta = Vector2.zero;
            var vlg = contGo.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(8, 8, 8, 8); vlg.spacing = 14;
            vlg.childControlWidth = true; vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
            contGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var sr = chatGo.GetComponent<ScrollRect>();
            sr.content = contRt; sr.viewport = chatRt;
            sr.horizontal = false; sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 30;

            var chat = chatGo.AddComponent<ChatUI>();
            chat.scroll = sr;
            chat.contenido = contRt;
            chat.spriteBurbuja = Redondeado();

            // Preguntas sugeridas (ScrollRect horizontal)
            var chipsGo = new GameObject("Sugeridas", typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
            chipsGo.transform.SetParent(panel.transform, false);
            Rect(chipsGo, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 180), new Vector2(-32, 110));
            chipsGo.GetComponent<Image>().color = new Color(0, 0, 0, 0);

            var chipsCont = new GameObject("Chips", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
            chipsCont.transform.SetParent(chipsGo.transform, false);
            var ccRt = (RectTransform)chipsCont.transform;
            ccRt.anchorMin = new Vector2(0, 0); ccRt.anchorMax = new Vector2(0, 1); ccRt.pivot = new Vector2(0, 0.5f);
            ccRt.anchoredPosition = Vector2.zero; ccRt.sizeDelta = Vector2.zero;
            var hlg = chipsCont.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 14; hlg.padding = new RectOffset(4, 4, 14, 14);
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = true; hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;
            chipsCont.GetComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            var csr = chipsGo.GetComponent<ScrollRect>();
            csr.content = ccRt; csr.viewport = (RectTransform)chipsGo.transform;
            csr.horizontal = true; csr.vertical = false;
            csr.movementType = ScrollRect.MovementType.Clamped;

            // Fila de entrada: texto + enviar + microfono
            var campoGo = TMP_DefaultControls.CreateInputField(RecursosUI());
            campoGo.name = "CampoPregunta";
            campoGo.transform.SetParent(panel.transform, false);
            Ubicar(campoGo, Ancla.AbajoIzq, 16, 36, 540, 120);
            campoGo.GetComponent<Image>().color = CristalClaro;
            var campo = campoGo.GetComponent<TMP_InputField>();
            campo.textComponent.fontSize = 34;
            campo.textComponent.color = Color.white;
            if (campo.placeholder is TMP_Text ph) { ph.text = "Escribe tu pregunta..."; ph.fontSize = 34; ph.color = new Color(1, 1, 1, 0.5f); }

            var enviar = Boton(panel.transform, "BtnEnviar", "Enviar", 30, Azul, Color.white);
            enviar.GetComponentInChildren<TMP_Text>().fontStyle = FontStyles.Bold;
            Ubicar(enviar.gameObject, Ancla.AbajoIzq, 568, 36, 170, 120);

            var mic = Boton(panel.transform, "BtnMic", "Mantén\npara hablar", 26, Ambar, Oscuro);
            Ubicar(mic.gameObject, Ancla.AbajoDer, -16, 36, 270, 120);
            var mantener = mic.gameObject.AddComponent<BotonMantener>();

            // ---------------- Conexiones ----------------
            var ui = canvas.gameObject.AddComponent<GuideUI>();
            ui.cerebro = cerebro;
            ui.kb = kb;
            ui.spawner = spawner;
            ui.disponibilidadAR = disponibilidad;
            ui.acompanante = acompanante;
            ui.modoSinAR = modoSinAR;
            ui.geo = geo;
            ui.textoEstacion = estacion;
            ui.textoProgreso = progreso;
            ui.mapa = mapa;
            ui.botonEmergencia = emergencia;
            ui.chat = chat;
            ui.contenedorChips = chipsCont.transform;
            ui.prefabChip = prefabChip;
            ui.campoPregunta = campo;
            ui.botonEnviar = enviar;
            ui.botonMic = mantener;
            ui.textoMic = mic.GetComponentInChildren<TMP_Text>();
            ui.botonSilenciar = silenciar;
            ui.botonAcompanar = acompanar;
            ui.textoAcompanar = acompanar.GetComponentInChildren<TMP_Text>();
            ui.botonVenAqui = venAqui;
            ui.panelInferior = (RectTransform)panel.transform;
            ui.ocultablesAlPlegar = new[] { chatGo };
            ui.botonPlegar = plegar;
            EditorUtility.SetDirty(ui);
            return ui;
        }

        static Button CrearPrefabChip()
        {
            var go = new GameObject("Chip", typeof(RectTransform), typeof(Image), typeof(Button), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            var img = go.GetComponent<Image>();
            img.sprite = Redondeado();
            img.type = Image.Type.Sliced;
            img.color = new Color(0.13f, 0.40f, 0.85f, 0.92f);   // azul "glass"
            go.GetComponent<Button>().targetGraphic = img;
            var h = go.GetComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(26, 26, 10, 10);
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childControlWidth = true; h.childControlHeight = true;
            h.childForceExpandWidth = false; h.childForceExpandHeight = false;
            var le = go.GetComponent<LayoutElement>();
            le.minHeight = 76; le.preferredHeight = 76;

            var txt = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            txt.transform.SetParent(go.transform, false);
            var tmp = txt.GetComponent<TextMeshProUGUI>();
            tmp.text = "¿Pregunta sugerida?";
            tmp.fontSize = 26;
            tmp.color = Color.white;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, RutaPrefabChip);
            UnityEngine.Object.DestroyImmediate(go);
            return prefab.GetComponent<Button>();
        }

        // =====================================================================
        // Modo sin AR: camara normal, foto de la estacion de fondo y Frai en 3D
        // =====================================================================
        static ModoSinAR CrearModoSinAR(GameObject raiz, GameObject prefabFrailejon, KnowledgeBase kb, Texture2D[] fotos)
        {
            var camGo = new GameObject("CamaraSinAR", typeof(Camera), typeof(AudioListener));
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(raiz.transform);
            // Encuadre pensado para que Frai quede en la franja libre entre la barra de arriba y el panel de abajo.
            camGo.transform.position = new Vector3(0, 0.7f, -3f);
            camGo.transform.LookAt(new Vector3(0, 0.02f, 0));
            var cam = camGo.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.50f, 0.72f, 0.92f);   // cielo de paramo

            var fijo = (GameObject)PrefabUtility.InstantiatePrefab(prefabFrailejon);
            fijo.name = "FrailejonFijo";
            fijo.transform.SetParent(raiz.transform);
            fijo.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0, 180, 0));

            // Fondo: un plano con la foto de la estacion, detras de Frai.
            var fondo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            fondo.name = "FondoEstacion";
            UnityEngine.Object.DestroyImmediate(fondo.GetComponent<Collider>());
            fondo.transform.SetParent(raiz.transform);
            fondo.transform.position = camGo.transform.position + camGo.transform.forward * 7f;
            fondo.transform.rotation = camGo.transform.rotation;
            var primera = fotos.FirstOrDefault(f => f != null);
            float aspecto = primera != null ? primera.width / (float)primera.height : 1.5f;
            fondo.transform.localScale = new Vector3(8.6f * Mathf.Max(aspecto, 0.6f), 8.6f, 1f);

            AssetDatabase.DeleteAsset(RutaMaterialFondo);
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture");
            var mat = new Material(shader);
            if (primera != null) mat.mainTexture = primera;
            AssetDatabase.CreateAsset(mat, RutaMaterialFondo);
            var r = fondo.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;

            var modo = raiz.AddComponent<ModoSinAR>();
            modo.kb = kb;
            modo.camara = cam;
            modo.fondo = r;
            modo.fotos = fotos;
            return modo;
        }

        // =====================================================================
        // Configuracion del proyecto
        // =====================================================================
        static void AgregarEscenaAlBuild(string ruta)
        {
            var lista = EditorBuildSettings.scenes.Where(s => s.path != ruta).ToList();
            lista.Insert(0, new EditorBuildSettingsScene(ruta, true));
            EditorBuildSettings.scenes = lista.ToArray();
        }

        /// <summary>Devuelve true si hay que reiniciar Unity (cambio de Active Input Handling).</summary>
        static bool ConfigurarPlayer()
        {
            PlayerSettings.productName = "Guia Frailejon";
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            if ((int)PlayerSettings.Android.minSdkVersion < 24)
                PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)24;

            // GPS (Input.location) e inclinacion (Input.acceleration) necesitan el sistema de entrada clasico.
            // 'Both' deja funcionando tambien el nuevo Input System (lo usa la interfaz).
            var ajustes = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset").FirstOrDefault();
            if (ajustes != null)
            {
                var so = new SerializedObject(ajustes);
                var p = so.FindProperty("activeInputHandler");
                if (p != null && p.intValue != 2)
                {
                    p.intValue = 2;   // 0 = clasico, 1 = nuevo, 2 = ambos
                    so.ApplyModifiedProperties();
                    return true;
                }
            }
            return false;
        }

        static void CrearCarpeta(string ruta)
        {
            if (AssetDatabase.IsValidFolder(ruta)) return;
            string padre = Path.GetDirectoryName(ruta).Replace('\\', '/');
            AssetDatabase.CreateFolder(padre, Path.GetFileName(ruta));
        }
    }
}
#endif
