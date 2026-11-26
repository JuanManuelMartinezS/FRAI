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
                CrearCarpeta(EstiloUI.Carpeta);

                GameObject prefabFrailejon = CrearPrefabFrailejon();
                GameObject prefabCondor = CrearPrefabCondor();
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
                bool arOk = CrearObjetosAR(raizAR.transform, libreria, prefabFrailejon, prefabCondor, kb, out FrailejonSpawner spawner, out FraiAcompanante acompanante);

                // ---- Modo sin AR ----
                var raizSinAR = new GameObject("RaizSinAR");
                var modoSinAR = CrearModoSinAR(raizSinAR, prefabFrailejon, prefabCondor, kb, fotos);
                disponibilidad.raizAR = raizAR;
                disponibilidad.raizSinAR = raizSinAR;

                // ---- Vision artificial y clima ----
                var clasificador = sistema.AddComponent<ClasificadorEspecies>();
                clasificador.metadatos = AssetDatabase.LoadAssetAtPath<TextAsset>(ActivarVision.Carpeta + "/vision.json");
                clasificador.fichas = AssetDatabase.LoadAssetAtPath<TextAsset>(ActivarVision.Carpeta + "/especies_info.json");
                var captura = sistema.AddComponent<CapturaCamara>();
                captura.camaraAR = UnityEngine.Object.FindAnyObjectByType<ARCameraManager>(FindObjectsInactive.Include);
                captura.disponibilidadAR = disponibilidad;
                var fuenteClima = sistema.AddComponent<FuenteClima>();

                // ---- Interfaz ----
                EditorUtility.DisplayProgressBar("Guia Frailejon", "Creando la interfaz...", 0.7f);
                CrearEventSystem();
                CrearInterfaz(cerebro, kb, spawner, acompanante, disponibilidad, modoSinAR, geo, prefabChip, clasificador, captura, fuenteClima);
                if (ActivarVision.TieneSimbolo()) ActivarVision.Conectar(clasificador);

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
                    msg += "\n\nIMPORTANTE: se cambió 'Active Input Handling' a 'Input System Package (New)' ('Both' no está soportado en Android). CIERREN Y VUELVAN A ABRIR UNITY antes de compilar.";
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

        /// <summary>Condor desde Assets/Condor (.glb). Sin normalizar: el modelo se usa a su tamano original (metros).</summary>
        static GameObject CrearPrefabCondor()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Condor")) return null;
            string ruta = AssetDatabase.FindAssets("", new[] { "Assets/Condor" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p => new[] { ".glb", ".gltf", ".fbx" }.Contains(Path.GetExtension(p).ToLowerInvariant()));
            if (ruta == null) return null;
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(ruta);
            if (asset == null) { Debug.LogWarning("[ArmarEscena] No se pudo cargar el condor " + ruta); return null; }

            var raiz = new GameObject("CondorGuia");
            var cuerpo = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            cuerpo.name = "Modelo";
            cuerpo.transform.SetParent(raiz.transform, false);
            var anim = raiz.AddComponent<FrailejonAnimator>();
            anim.roseta = cuerpo.transform;
            var prefab = PrefabUtility.SaveAsPrefabAsset(raiz, CarpetaPrefabs + "/CondorGuia.prefab");
            UnityEngine.Object.DestroyImmediate(raiz);
            Debug.Log("[ArmarEscena] Condor listo: " + ruta);
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
        static bool CrearObjetosAR(Transform raizAR, XRReferenceImageLibrary lib, GameObject prefab, GameObject prefabCondor, KnowledgeBase kb,
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
            spawner.prefabCondor = prefabCondor;
            spawner.kb = kb;

            // Deteccion del piso (para que Frai camine al lado del visitante).
            var planos = origen.gameObject.AddComponent<ARPlaneManager>();
            planos.requestedDetectionMode = PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical;   // piso + paredes/muebles
            origen.gameObject.AddComponent<ARRaycastManager>();
            acompanante = origen.gameObject.AddComponent<FraiAcompanante>();
            acompanante.prefabFrailejon = prefab;
            acompanante.prefabCondor = prefabCondor;
            spawner.acompanante = acompanante;
            return true;
        }

        // =====================================================================
        // UI  (estilo "cristal" azul noche; sprites e iconos en EstiloUI)
        // =====================================================================
        static Sprite SpRed => EstiloUI.Redondeado();
        static Sprite SpPil => EstiloUI.Pildora();
        static Sprite Ico(string n) => EstiloUI.Icono(n);

        static readonly string[] NombresIconos =
        {
            "mic", "enviar", "silencio", "camara", "chat", "nube", "sol", "solnube", "lluvia", "niebla", "gota", "viento",
            "presion", "termometro", "antena", "pin", "alerta", "cerrar", "atras", "chevron", "hoja", "huella", "objetivo",
            "check", "info", "montana", "esquina",
        };

        static TMP_DefaultControls.Resources RecursosUI() => new TMP_DefaultControls.Resources
        {
            standard = SpPil,
            background = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd"),
            inputField = SpPil,
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

        enum Ancla { ArribaCentro, ArribaIzq, ArribaDer, AbajoCentro, AbajoIzq, AbajoDer, Centro }

        static void Ubicar(GameObject go, Ancla a, float x, float y, float w, float h)
        {
            var rt = go.GetComponent<RectTransform>();
            if (rt == null) rt = go.AddComponent<RectTransform>();
            Vector2 p = a switch
            {
                Ancla.ArribaCentro => new Vector2(0.5f, 1), Ancla.ArribaIzq => new Vector2(0, 1), Ancla.ArribaDer => new Vector2(1, 1),
                Ancla.AbajoCentro => new Vector2(0.5f, 0), Ancla.AbajoIzq => new Vector2(0, 0), Ancla.AbajoDer => new Vector2(1, 0),
                _ => new Vector2(0.5f, 0.5f),
            };
            rt.anchorMin = rt.anchorMax = rt.pivot = p;
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
        }

        static void Rect(GameObject go, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.pivot = pivot;
            rt.anchoredPosition = pos; rt.sizeDelta = size;
        }

        /// <summary>Ocupa todo el padre, con margenes (positivos hacia adentro).</summary>
        static RectTransform Estirar(GameObject go, float izq = 0, float abajo = 0, float der = 0, float arriba = 0)
        {
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(izq, abajo); rt.offsetMax = new Vector2(-der, -arriba);
            return rt;
        }

        /// <summary>Fila de ancho completo anclada arriba: x desde la izquierda, margen derecho 'der'.</summary>
        static void FilaArriba(GameObject go, float x, float y, float der, float h)
        {
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(-(x + der), h);
        }

        static GameObject Nodo(string nombre, Transform padre)
        {
            var go = new GameObject(nombre, typeof(RectTransform));
            go.transform.SetParent(padre, false);
            return go;
        }

        static Image Img(string nombre, Transform padre, Sprite sp, Color c, bool raycast = false)
        {
            var go = new GameObject(nombre, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(padre, false);
            var img = go.GetComponent<Image>();
            img.sprite = sp;
            img.type = sp != null && sp.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            img.color = c;
            img.raycastTarget = raycast;
            return img;
        }

        static Image Icono(Transform padre, string icono, Color c, Ancla a, float x, float y, float tam)
        {
            var img = Img("Icono", padre, Ico(icono), c);
            Ubicar(img.gameObject, a, x, y, tam, tam);
            return img;
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
            t.overflowMode = TextOverflowModes.Truncate;
            t.raycastTarget = false;
            return t;
        }

        /// <summary>Tarjeta con sombra suave. El contenido se agrega como hijo del objeto devuelto.</summary>
        static GameObject Tarjeta(string nombre, Transform padre, Color color, bool sombra = true, bool bloquear = true)
        {
            var go = Nodo(nombre, padre);
            if (sombra)
            {
                var s = Img("Sombra", go.transform, EstiloUI.Sombra(), new Color(0, 0, 0, 0.9f));
                Estirar(s.gameObject, -26, -34, -26, -18);
            }
            var f = Img("Fondo", go.transform, SpRed, color, bloquear);
            Estirar(f.gameObject);
            return go;
        }

        static Button BotonCircular(Transform padre, string nombre, string icono, string texto, Color fondo, Color frente, float diam)
        {
            var img = Img(nombre, padre, SpPil, fondo, true);
            ((RectTransform)img.transform).sizeDelta = new Vector2(diam, diam);
            var b = img.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            if (icono != null) Icono(img.transform, icono, frente, Ancla.Centro, 0, 0, diam * 0.5f);
            if (texto != null)
            {
                var t = Texto(img.transform, "Texto", texto, diam * 0.3f, Ancla.Centro, 0, 0, diam, diam);
                t.color = frente; t.fontStyle = FontStyles.Bold;
            }
            return b;
        }

        static Button BotonPildora(Transform padre, string nombre, string texto, string icono, Color fondo, Color frente, float tam)
        {
            var img = Img(nombre, padre, SpPil, fondo, true);
            var b = img.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            var h = img.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(32, 32, 0, 0); h.spacing = 14;
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childControlWidth = true; h.childControlHeight = true;
            h.childForceExpandWidth = false; h.childForceExpandHeight = false;
            if (icono != null)
            {
                var ic = Img("Icono", img.transform, Ico(icono), frente);
                var le = ic.gameObject.AddComponent<LayoutElement>();
                le.minWidth = le.preferredWidth = le.minHeight = le.preferredHeight = tam * 1.4f;
            }
            var t = Texto(img.transform, "Texto", texto, tam, Ancla.Centro, 0, 0, 10, 10);
            t.color = frente; t.fontStyle = FontStyles.Bold;
            t.textWrappingMode = TextWrappingModes.NoWrap; t.overflowMode = TextOverflowModes.Overflow;
            return b;
        }

        /// <summary>Boton flotante redondo con etiqueta debajo (Silenciar, Acompañame...).</summary>
        static Button Fab(Transform padre, string nombre, string icono, string etiqueta, Color fondo, out TMP_Text texto)
        {
            var go = new GameObject(nombre, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(padre, false);
            ((RectTransform)go.transform).sizeDelta = new Vector2(150, 156);
            go.GetComponent<Image>().color = new Color(0, 0, 0, 0);   // area tactil
            var circ = Img("Circulo", go.transform, SpPil, fondo);
            Ubicar(circ.gameObject, Ancla.ArribaCentro, 0, 0, 108, 108);
            Icono(circ.transform, icono, Color.white, Ancla.Centro, 0, 0, 54);
            var pil = Img("FondoEtiqueta", go.transform, SpPil, new Color(0.02f, 0.06f, 0.14f, 0.55f));
            Ubicar(pil.gameObject, Ancla.AbajoCentro, 0, 0, 148, 38);
            texto = Texto(pil.transform, "Etiqueta", etiqueta, 21, Ancla.Centro, 0, 0, 144, 38);
            texto.fontStyle = FontStyles.Bold;
            texto.enableAutoSizing = true; texto.fontSizeMin = 16; texto.fontSizeMax = 21;
            var b = go.GetComponent<Button>();
            b.targetGraphic = circ;
            return b;
        }

        static GuideUI CrearInterfaz(GuideBrain cerebro, KnowledgeBase kb, FrailejonSpawner spawner, FraiAcompanante acompanante,
                                     ARAvailability disponibilidad, ModoSinAR modoSinAR, GeoLocalizador geo, Button prefabChip,
                                     ClasificadorEspecies clasificador, CapturaCamara captura, FuenteClima fuenteClima)
        {
            var canvas = CrearCanvas("Canvas", null, 0);
            var c = canvas.transform;
            Color blanco = Color.white;

            // ---------------- Fondos que llegan al borde de la pantalla ----------------
            var fondoCam = new GameObject("FondoCamara", typeof(RectTransform), typeof(RawImage));
            fondoCam.transform.SetParent(c, false);
            Ubicar(fondoCam, Ancla.Centro, 0, 0, 1080, 1920);
            fondoCam.GetComponent<RawImage>().raycastTarget = false;
            fondoCam.SetActive(false);
            if (captura) captura.fondoCamara = fondoCam.GetComponent<RawImage>();

            var colorDeg = new Color(0.02f, 0.07f, 0.16f, 0.80f);
            var degArriba = Img("DegradadoSuperior", c, EstiloUI.Degradado(), colorDeg);
            Rect(degArriba.gameObject, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(0, 560));
            var degAbajo = Img("DegradadoInferior", c, EstiloUI.Degradado(), colorDeg);
            Rect(degAbajo.gameObject, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0.5f), new Vector2(0, 200), new Vector2(0, 400));
            degAbajo.transform.localEulerAngles = new Vector3(0, 0, 180);

            // ---------------- Zona segura (sin notch ni barra de gestos) ----------------
            var segura = Nodo("ZonaSegura", c);
            Estirar(segura);
            segura.AddComponent<AreaSegura>();
            var s = segura.transform;

            var pGuia = Nodo("PestanaGuia", s); Estirar(pGuia);
            var pExplorar = Nodo("PestanaExplorar", s); Estirar(pExplorar);
            var pClima = Nodo("PestanaClima", s); Estirar(pClima);

            // =================================================================
            // PESTANA GUIA
            // =================================================================
            var g = pGuia.transform;

            // ---- Encabezado: marca + estacion + SOS ----
            Icono(g, "montana", blanco, Ancla.ArribaIzq, 26, -22, 96);
            var marca = Texto(g, "TextoParque", "Parque Nacional\n<b>Los Nevados</b>", 29, Ancla.ArribaIzq, 132, -20, 300, 100, TextAlignmentOptions.Left);
            marca.textWrappingMode = TextWrappingModes.NoWrap;
            marca.lineSpacing = -8;

            var emergencia = BotonCircular(g, "BtnEmergencia", null, "SOS", EstiloUI.Rojo, blanco, 100);
            Ubicar(emergencia.gameObject, Ancla.ArribaDer, -26, -20, 100, 100);

            var pastilla = Img("PastillaEstacion", g, SpPil, EstiloUI.Noche);
            Ubicar(pastilla.gameObject, Ancla.ArribaDer, -142, -20, 360, 100);
            Icono(pastilla.transform, "pin", EstiloUI.Ambar, Ancla.ArribaIzq, 26, -24, 52);
            var estacion = Texto(pastilla.transform, "TextoEstacion", "", 30, Ancla.ArribaIzq, 90, 0, 250, 100, TextAlignmentOptions.Left);
            estacion.fontStyle = FontStyles.Bold;
            estacion.lineSpacing = -12;
            estacion.enableAutoSizing = true; estacion.fontSizeMin = 20; estacion.fontSizeMax = 30;

            // ---- Recorrido (stepper de estaciones) ----
            var recorrido = Tarjeta("Recorrido", g, new Color(0.035f, 0.10f, 0.22f, 0.70f), sombra: false, bloquear: false);
            Rect(recorrido, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -138), new Vector2(-52, 150));
            var mapaGo = Nodo("Mapa", recorrido.transform);
            Estirar(mapaGo, 6, 2, 6, 2);
            var mapa = mapaGo.AddComponent<MiniMapa>();
            mapa.kb = kb;
            mapa.geo = geo;
            mapa.circulo = SpPil;

            var progPil = Img("PastillaProgreso", g, SpPil, new Color(0.02f, 0.06f, 0.14f, 0.55f));
            Ubicar(progPil.gameObject, Ancla.ArribaIzq, 26, -304, 640, 54);
            var progreso = Texto(progPil.transform, "TextoProgreso", "", 24, Ancla.Centro, 0, 0, 10, 10, TextAlignmentOptions.Left);
            Estirar(progreso.gameObject, 26, 0, 20, 0);
            progreso.color = EstiloUI.TextoSuave;
            progreso.enableAutoSizing = true; progreso.fontSizeMin = 17; progreso.fontSizeMax = 24;
            progreso.textWrappingMode = TextWrappingModes.NoWrap;

            // ---- Botones flotantes (columna derecha; los ocultos no dejan hueco) ----
            var flot = Nodo("Flotantes", g);
            Ubicar(flot, Ancla.ArribaDer, -16, -300, 152, 740);
            var vf = flot.AddComponent<VerticalLayoutGroup>();
            vf.spacing = 14; vf.childAlignment = TextAnchor.UpperCenter;
            vf.childControlWidth = false; vf.childControlHeight = false;
            vf.childForceExpandWidth = false; vf.childForceExpandHeight = false;
            var silenciar = Fab(flot.transform, "BtnSilenciar", "silencio", "Silenciar", EstiloUI.Noche, out _);
            var acompanar = Fab(flot.transform, "BtnAcompanar", "huella", "Acompáñame", EstiloUI.Turquesa, out var textoAcompanar);
            var venAqui = Fab(flot.transform, "BtnVenAqui", "objetivo", "Ven aquí", EstiloUI.Noche, out _);
            var cambiar = Fab(flot.transform, "BtnCambiarAvatar", "hoja", "Ser Cóndor", EstiloUI.Ambar, out var textoCambiar);
            var selector = cambiar.gameObject.AddComponent<SelectorAvatar>();
            selector.etiqueta = textoCambiar;

            // ---- Panel de conversacion (plegable) ----
            var panel = Tarjeta("PanelInferior", g, EstiloUI.Noche);
            Rect(panel, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 168), new Vector2(-40, 780));
            var pt = panel.transform;

            var plegarImg = Img("BtnPlegar", pt, null, new Color(0, 0, 0, 0), true);
            Ubicar(plegarImg.gameObject, Ancla.ArribaCentro, 0, 0, 260, 56);
            var plegar = plegarImg.gameObject.AddComponent<Button>();
            plegar.targetGraphic = plegarImg;
            var chevron = Icono(plegarImg.transform, "chevron", new Color(1, 1, 1, 0.75f), Ancla.Centro, 0, -2, 46);

            // Conversacion (ScrollRect vertical)
            var chatGo = new GameObject("Chat", typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
            chatGo.transform.SetParent(pt, false);
            var chatRt = Estirar(chatGo, 12, 280, 12, 52);
            chatGo.GetComponent<Image>().color = new Color(0, 0, 0, 0);   // invisible, pero permite arrastrar

            var contGo = new GameObject("Contenido", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contGo.transform.SetParent(chatGo.transform, false);
            var contRt = (RectTransform)contGo.transform;
            contRt.anchorMin = new Vector2(0, 1); contRt.anchorMax = new Vector2(1, 1); contRt.pivot = new Vector2(0.5f, 1);
            contRt.anchoredPosition = Vector2.zero; contRt.sizeDelta = Vector2.zero;
            var vlg = contGo.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(8, 8, 8, 8); vlg.spacing = 16;
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
            chat.spriteBurbuja = SpRed;
            chat.anchoBurbuja = 860;
            chat.colorUsuario = EstiloUI.Azul;
            chat.spriteAvatarFondo = SpPil;
            chat.spriteAvatarIcono = Ico("hoja");
            chat.colorAvatar = EstiloUI.Turquesa;

            // Preguntas sugeridas (ScrollRect horizontal)
            var chipsGo = new GameObject("Sugeridas", typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
            chipsGo.transform.SetParent(pt, false);
            Rect(chipsGo, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 148), new Vector2(-28, 104));
            chipsGo.GetComponent<Image>().color = new Color(0, 0, 0, 0);

            var chipsCont = new GameObject("Chips", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
            chipsCont.transform.SetParent(chipsGo.transform, false);
            var ccRt = (RectTransform)chipsCont.transform;
            ccRt.anchorMin = new Vector2(0, 0); ccRt.anchorMax = new Vector2(0, 1); ccRt.pivot = new Vector2(0, 0.5f);
            ccRt.anchoredPosition = Vector2.zero; ccRt.sizeDelta = Vector2.zero;
            var hlg = chipsCont.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 12; hlg.padding = new RectOffset(4, 4, 16, 16);
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = true; hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;
            chipsCont.GetComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            var csr = chipsGo.GetComponent<ScrollRect>();
            csr.content = ccRt; csr.viewport = (RectTransform)chipsGo.transform;
            csr.horizontal = true; csr.vertical = false;
            csr.movementType = ScrollRect.MovementType.Clamped;

            // Estado de la voz ("Escuchando..."), flotando sobre el panel
            var estadoVoz = Img("EstadoVoz", pt, SpPil, EstiloUI.Ambar);
            Ubicar(estadoVoz.gameObject, Ancla.ArribaCentro, 0, 78, 600, 62);
            Icono(estadoVoz.transform, "mic", EstiloUI.TextoOscuro, Ancla.ArribaIzq, 24, -11, 40);
            var textoVoz = Texto(estadoVoz.transform, "Texto", "", 26, Ancla.Centro, 0, 0, 10, 10);
            Estirar(textoVoz.gameObject, 70, 0, 24, 0);
            textoVoz.color = EstiloUI.TextoOscuro; textoVoz.fontStyle = FontStyles.Bold;
            estadoVoz.gameObject.SetActive(false);

            // Fila de entrada: texto + enviar + microfono
            var fila = Nodo("FilaEntrada", pt);
            Rect(fila, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 24), new Vector2(-32, 112));
            var hf = fila.AddComponent<HorizontalLayoutGroup>();
            hf.spacing = 14; hf.childAlignment = TextAnchor.MiddleLeft;
            hf.childControlWidth = true; hf.childControlHeight = true;
            hf.childForceExpandWidth = false; hf.childForceExpandHeight = true;

            var campoGo = TMP_DefaultControls.CreateInputField(RecursosUI());
            campoGo.name = "CampoPregunta";
            campoGo.transform.SetParent(fila.transform, false);
            campoGo.AddComponent<LayoutElement>().flexibleWidth = 1;
            var campoImg = campoGo.GetComponent<Image>();
            campoImg.color = EstiloUI.Cristal; campoImg.type = Image.Type.Sliced;
            var campo = campoGo.GetComponent<TMP_InputField>();
            var area = campo.textViewport;
            if (area) { area.offsetMin = new Vector2(36, 10); area.offsetMax = new Vector2(-28, -10); }
            campo.textComponent.fontSize = 30;
            campo.textComponent.color = blanco;
            campo.textComponent.alignment = TextAlignmentOptions.MidlineLeft;
            campo.caretColor = EstiloUI.Ambar;
            campo.customCaretColor = true;
            campo.selectionColor = new Color(0.16f, 0.52f, 1f, 0.5f);
            if (campo.placeholder is TMP_Text ph)
            {
                ph.text = "Pregúntale a Frai…"; ph.fontSize = 30; ph.fontStyle = FontStyles.Normal;
                ph.color = new Color(1, 1, 1, 0.5f); ph.alignment = TextAlignmentOptions.MidlineLeft;
            }

            var enviar = BotonCircular(fila.transform, "BtnEnviar", "enviar", null, EstiloUI.Azul, blanco, 112);
            var le1 = enviar.gameObject.AddComponent<LayoutElement>(); le1.preferredWidth = le1.minWidth = 112;
            var mic = BotonCircular(fila.transform, "BtnMic", "mic", null, EstiloUI.Ambar, EstiloUI.TextoOscuro, 112);
            var le2 = mic.gameObject.AddComponent<LayoutElement>(); le2.preferredWidth = le2.minWidth = 112;
            var mantener = mic.gameObject.AddComponent<BotonMantener>();

            // =================================================================
            // PESTANA EXPLORAR (vision artificial)
            // =================================================================
            var e = pExplorar.transform;
            var volverE = BotonCircular(e, "BtnVolver", "atras", null, EstiloUI.Noche, blanco, 96);
            Ubicar(volverE.gameObject, Ancla.ArribaIzq, 26, -22, 96, 96);
            var tituloE = Texto(e, "Titulo", "Explorar", 42, Ancla.ArribaCentro, 0, -22, 620, 96);
            tituloE.fontStyle = FontStyles.Bold;
            var ayuda = Texto(e, "TextoAyuda", "Apunta la cámara a una planta o animal", 27, Ancla.ArribaCentro, 0, -122, 900, 44);
            ayuda.color = EstiloUI.TextoSuave;

            var aviso = Img("Aviso", e, SpPil, new Color(1f, 0.79f, 0.16f, 0.94f));
            Ubicar(aviso.gameObject, Ancla.ArribaCentro, 0, -178, 940, 70);
            Icono(aviso.transform, "alerta", EstiloUI.TextoOscuro, Ancla.ArribaIzq, 26, -15, 40);
            var textoAviso = Texto(aviso.transform, "Texto", "", 23, Ancla.Centro, 0, 0, 10, 10, TextAlignmentOptions.Left);
            Estirar(textoAviso.gameObject, 82, 4, 30, 4);
            textoAviso.color = EstiloUI.TextoOscuro;
            textoAviso.enableAutoSizing = true; textoAviso.fontSizeMin = 16; textoAviso.fontSizeMax = 23;
            aviso.gameObject.SetActive(false);

            // Marco de escaneo
            const float Marco = 620;
            var marco = Nodo("MarcoEscaneo", e);
            Ubicar(marco, Ancla.Centro, 0, 190, Marco, Marco);
            var esquinas = new Image[4];
            Vector2[] anclas = { new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 0) };
            float[] giros = { 0, -90, 180, 90 };
            for (int i = 0; i < 4; i++)
            {
                var es = Img("Esquina" + i, marco.transform, Ico("esquina"), blanco);
                var ert = (RectTransform)es.transform;
                ert.anchorMin = ert.anchorMax = anclas[i];
                ert.pivot = new Vector2(0.5f, 0.5f);
                ert.sizeDelta = new Vector2(150, 150);
                ert.anchoredPosition = new Vector2(anclas[i].x > 0 ? -75 : 75, anclas[i].y > 0 ? -75 : 75);
                ert.localEulerAngles = new Vector3(0, 0, giros[i]);
                esquinas[i] = es;
            }
            var linea = Img("LineaEscaneo", marco.transform, EstiloUI.Brillo(), new Color(0.35f, 0.95f, 0.75f, 0.85f));
            Ubicar(linea.gameObject, Ancla.Centro, 0, 0, Marco * 0.85f, 36);

            var etiqueta = Img("EtiquetaEspecie", marco.transform, SpPil, new Color(0.08f, 0.42f, 0.24f, 0.94f));
            var etRt = (RectTransform)etiqueta.transform;
            etRt.anchorMin = etRt.anchorMax = new Vector2(0.5f, 1); etRt.pivot = new Vector2(0.5f, 0);
            etRt.anchoredPosition = new Vector2(0, 22); etRt.sizeDelta = new Vector2(560, 108);
            var hojaCirc = Img("CirculoHoja", etiqueta.transform, SpPil, new Color(1, 1, 1, 0.2f));
            Ubicar(hojaCirc.gameObject, Ancla.ArribaIzq, 16, -14, 80, 80);
            Icono(hojaCirc.transform, "hoja", blanco, Ancla.Centro, 0, 0, 46);
            var textoEt = Texto(etiqueta.transform, "Texto", "", 34, Ancla.Centro, 0, 0, 10, 10, TextAlignmentOptions.Left);
            Estirar(textoEt.gameObject, 112, 6, 24, 6);
            textoEt.enableAutoSizing = true; textoEt.fontSizeMin = 22; textoEt.fontSizeMax = 34;
            textoEt.lineSpacing = -10;
            etiqueta.gameObject.SetActive(false);

            // Tarjeta de resultado
            var res = Tarjeta("TarjetaResultado", e, EstiloUI.Noche);
            Rect(res, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 168), new Vector2(-40, 420));
            var rt = res.transform;
            var mascara = Img("Miniatura", rt, SpRed, blanco);
            Ubicar(mascara.gameObject, Ancla.ArribaIzq, 24, -24, 200, 200);
            mascara.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var fondoMini = Img("FondoMini", mascara.transform, null, new Color(1, 1, 1, 0.08f));
            Estirar(fondoMini.gameObject);
            Icono(mascara.transform, "camara", new Color(1, 1, 1, 0.3f), Ancla.Centro, 0, 0, 80);
            var mini = new GameObject("Imagen", typeof(RectTransform), typeof(RawImage));
            mini.transform.SetParent(mascara.transform, false);
            Estirar(mini);
            var miniImg = mini.GetComponent<RawImage>();
            miniImg.color = new Color(1, 1, 1, 0); miniImg.raycastTarget = false;

            var tipoPil = Img("Tipo", rt, SpPil, new Color(0.16f, 0.52f, 1f, 0.35f));
            Ubicar(tipoPil.gameObject, Ancla.ArribaIzq, 248, -24, 250, 46);
            var textoTipo = Texto(tipoPil.transform, "Texto", "BUSCANDO", 21, Ancla.Centro, 0, 0, 250, 46);
            textoTipo.fontStyle = FontStyles.Bold; textoTipo.characterSpacing = 4;
            textoTipo.enableAutoSizing = true; textoTipo.fontSizeMin = 14; textoTipo.fontSizeMax = 21;

            var nombre = Texto(rt, "Nombre", "Buscando especies…", 40, Ancla.ArribaIzq, 0, 0, 10, 10, TextAlignmentOptions.Left);
            FilaArriba(nombre.gameObject, 248, -80, 24, 56);
            nombre.fontStyle = FontStyles.Bold; nombre.textWrappingMode = TextWrappingModes.NoWrap;
            nombre.enableAutoSizing = true; nombre.fontSizeMin = 26; nombre.fontSizeMax = 40;
            var cientifico = Texto(rt, "Cientifico", "", 26, Ancla.ArribaIzq, 0, 0, 10, 10, TextAlignmentOptions.Left);
            FilaArriba(cientifico.gameObject, 248, -136, 24, 38);
            cientifico.color = EstiloUI.TextoSuave;

            var barra = Img("Barra", rt, SpPil, new Color(1, 1, 1, 0.15f));
            FilaArriba(barra.gameObject, 248, -188, 150, 14);
            var relleno = Img("Relleno", barra.transform, SpPil, EstiloUI.Azul);
            var rRt = Estirar(relleno.gameObject);
            rRt.anchorMax = new Vector2(0.5f, 1);
            var confianza = Texto(rt, "Confianza", "", 27, Ancla.ArribaDer, -24, -174, 120, 42, TextAlignmentOptions.Right);
            confianza.fontStyle = FontStyles.Bold;
            barra.gameObject.SetActive(false);

            var descripcion = Texto(rt, "Descripcion", "", 25, Ancla.ArribaIzq, 0, 0, 10, 10, TextAlignmentOptions.TopLeft);
            FilaArriba(descripcion.gameObject, 248, -216, 24, 104);
            descripcion.color = new Color(1, 1, 1, 0.9f);
            descripcion.enableAutoSizing = true; descripcion.fontSizeMin = 19; descripcion.fontSizeMax = 25;

            var preguntar = BotonPildora(rt, "BtnPreguntar", "Preguntar a Frai", "chat", EstiloUI.Azul, blanco, 28);
            Ubicar(preguntar.gameObject, Ancla.AbajoDer, -24, 22, 380, 84);
            preguntar.gameObject.SetActive(false);

            // =================================================================
            // PESTANA CLIMA Y ALERTAS
            // =================================================================
            var k = pClima.transform;
            var oscuro = Img("Oscurecer", k, null, new Color(0.02f, 0.06f, 0.14f, 0.74f), true);
            Estirar(oscuro.gameObject, -400, -400, -400, -400);
            var volverC = BotonCircular(k, "BtnVolver", "atras", null, EstiloUI.Noche, blanco, 96);
            Ubicar(volverC.gameObject, Ancla.ArribaIzq, 26, -22, 96, 96);
            var tituloC = Texto(k, "Titulo", "Clima y alertas", 42, Ancla.ArribaCentro, 0, -22, 640, 96);
            tituloC.fontStyle = FontStyles.Bold;
            var antenaCirc = Img("EstadoLoRa", k, SpPil, EstiloUI.Noche);
            Ubicar(antenaCirc.gameObject, Ancla.ArribaDer, -26, -22, 96, 96);
            var antenaBarra = Icono(antenaCirc.transform, "antena", EstiloUI.Verde, Ancla.Centro, 0, 0, 54);

            var scrollC = new GameObject("Contenido", typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
            scrollC.transform.SetParent(k, false);
            var scRt = Estirar(scrollC, 20, 168, 20, 136);
            scrollC.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            var listaC = new GameObject("Lista", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            listaC.transform.SetParent(scrollC.transform, false);
            var lRt = (RectTransform)listaC.transform;
            lRt.anchorMin = new Vector2(0, 1); lRt.anchorMax = new Vector2(1, 1); lRt.pivot = new Vector2(0.5f, 1);
            lRt.anchoredPosition = Vector2.zero; lRt.sizeDelta = Vector2.zero;
            var vc = listaC.GetComponent<VerticalLayoutGroup>();
            vc.padding = new RectOffset(0, 0, 6, 30); vc.spacing = 22;
            vc.childControlWidth = true; vc.childControlHeight = true;
            vc.childForceExpandWidth = true; vc.childForceExpandHeight = false;
            listaC.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scC = scrollC.GetComponent<ScrollRect>();
            scC.content = lRt; scC.viewport = scRt; scC.horizontal = false; scC.vertical = true;
            scC.movementType = ScrollRect.MovementType.Elastic; scC.scrollSensitivity = 30;
            var lc = listaC.transform;

            // 1. Lectura actual
            var actual = Tarjeta("Actual", lc, EstiloUI.Noche, sombra: false, bloquear: false);
            actual.AddComponent<LayoutElement>().preferredHeight = 330;
            var at = actual.transform;
            var iconoCond = Icono(at, "nube", blanco, Ancla.ArribaIzq, 34, -36, 150);
            var temp = Texto(at, "Temperatura", "--°", 118, Ancla.ArribaIzq, 196, -26, 340, 160, TextAlignmentOptions.Left);
            temp.fontStyle = FontStyles.Bold; temp.textWrappingMode = TextWrappingModes.NoWrap;
            var cond = Texto(at, "Condicion", "Sin datos", 34, Ancla.ArribaIzq, 36, -206, 500, 56, TextAlignmentOptions.Left);
            cond.enableAutoSizing = true; cond.fontSizeMin = 24; cond.fontSizeMax = 34;
            var divisor = Img("Divisor", at, null, new Color(1, 1, 1, 0.18f));
            Ubicar(divisor.gameObject, Ancla.ArribaIzq, 560, -40, 3, 250);
            TMP_Text Dato(string icono, string etiquetaDato, float y)
            {
                Icono(at, icono, EstiloUI.TextoSuave, Ancla.ArribaIzq, 600, y - 14, 50);
                var et = Texto(at, "Etiqueta", etiquetaDato, 22, Ancla.ArribaIzq, 670, y, 330, 32, TextAlignmentOptions.Left);
                et.color = EstiloUI.TextoSuave;
                var v = Texto(at, "Valor", "--", 34, Ancla.ArribaIzq, 670, y - 30, 330, 46, TextAlignmentOptions.Left);
                v.fontStyle = FontStyles.Bold;
                return v;
            }
            var hum = Dato("gota", "Humedad", -36);
            var vie = Dato("viento", "Viento", -128);
            var pre = Dato("presion", "Presión", -220);

            // 2. Estado del enlace LoRa
            var est = Tarjeta("EstadoEnlace", lc, EstiloUI.Noche, sombra: false, bloquear: false);
            est.AddComponent<LayoutElement>().preferredHeight = 124;
            var iconoEst = Icono(est.transform, "antena", EstiloUI.Verde, Ancla.ArribaIzq, 30, -30, 64);
            var textoEst = Texto(est.transform, "Titulo", "", 28, Ancla.ArribaIzq, 0, 0, 10, 10, TextAlignmentOptions.Left);
            FilaArriba(textoEst.gameObject, 120, -20, 24, 44);
            textoEst.fontStyle = FontStyles.Bold;
            var detalleEst = Texto(est.transform, "Detalle", "", 23, Ancla.ArribaIzq, 0, 0, 10, 10, TextAlignmentOptions.Left);
            FilaArriba(detalleEst.gameObject, 120, -66, 24, 38);
            detalleEst.color = EstiloUI.TextoSuave;

            // 3. Alertas
            var tAl = Texto(lc, "TituloAlertas", "Alertas", 34, Ancla.ArribaIzq, 0, 0, 10, 56, TextAlignmentOptions.BottomLeft);
            tAl.fontStyle = FontStyles.Bold;
            tAl.margin = new Vector4(8, 0, 0, 0);
            var alertas = Nodo("Alertas", lc);
            var va = alertas.AddComponent<VerticalLayoutGroup>();
            va.spacing = 16; va.childControlWidth = true; va.childControlHeight = true;
            va.childForceExpandWidth = true; va.childForceExpandHeight = false;

            // 4. Recomendaciones
            var recs = Img("Recomendaciones", lc, SpRed, EstiloUI.Noche);
            var vr = recs.gameObject.AddComponent<VerticalLayoutGroup>();
            vr.padding = new RectOffset(32, 32, 28, 30); vr.spacing = 18;
            vr.childControlWidth = true; vr.childControlHeight = true;
            vr.childForceExpandWidth = true; vr.childForceExpandHeight = false;
            var tRec = Texto(recs.transform, "Titulo", "Recomendaciones", 32, Ancla.ArribaIzq, 0, 0, 10, 10, TextAlignmentOptions.Left);
            tRec.fontStyle = FontStyles.Bold;

            // 5. Emergencia + nota
            var sosC = BotonPildora(lc, "BtnEmergencia", "¿Qué hago en una emergencia?", "alerta", EstiloUI.Rojo, blanco, 30);
            sosC.gameObject.AddComponent<LayoutElement>().preferredHeight = 100;
            var nota = Texto(lc, "Nota", "Los datos vienen del sensor meteorológico de la estación (LoRa). Las alertas son orientativas: " +
                                         "sigue siempre las indicaciones del guardaparque.", 22, Ancla.ArribaIzq, 0, 0, 10, 10, TextAlignmentOptions.Center);
            nota.color = new Color(0.76f, 0.86f, 1f, 0.7f);
            nota.margin = new Vector4(24, 0, 24, 0);

            // =================================================================
            // ELEMENTOS COMUNES
            // =================================================================
            // Diagnostico de AR (texto amarillo)
            var dbg = Texto(s, "TextoDebug", "", 20, Ancla.ArribaCentro, 0, -372, 1040, 56);
            dbg.color = new Color(1f, 0.9f, 0.4f);
            if (spawner) spawner.textoDebug = dbg;

            // Indicador de borde: "Frai >" cuando Frai queda fuera de la pantalla
            var indicador = Img("IndicadorFrai", s, SpPil, new Color(0.16f, 0.52f, 1f, 0.94f));
            var irt = (RectTransform)indicador.transform;
            irt.sizeDelta = new Vector2(200, 72);
            var textoInd = Texto(indicador.transform, "Texto", "Frai  >", 28, Ancla.Centro, 0, 0, 200, 72);
            textoInd.fontStyle = FontStyles.Bold;
            if (acompanante) { acompanante.indicador = irt; acompanante.textoIndicador = textoInd; }
            indicador.gameObject.SetActive(false);

            // Barra de navegacion inferior
            var nav = Tarjeta("Navegacion", s, EstiloUI.NocheSolida);
            Rect(nav, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 14), new Vector2(-40, 140));
            var tabs = Nodo("Pestanas", nav.transform);
            Estirar(tabs, 10, 0, 10, 0);
            var ht = tabs.AddComponent<HorizontalLayoutGroup>();
            ht.childControlWidth = true; ht.childControlHeight = true;
            ht.childForceExpandWidth = true; ht.childForceExpandHeight = true;
            NavegacionUI.Pestana Tab(string titulo, string icono, GameObject contenido)
            {
                var hit = Img("Tab" + titulo, tabs.transform, null, new Color(0, 0, 0, 0), true);
                var b = hit.gameObject.AddComponent<Button>();
                b.transition = Selectable.Transition.None;
                var res2 = Img("Resaltado", hit.transform, SpPil, new Color(0.16f, 0.52f, 1f, 0.38f));
                Ubicar(res2.gameObject, Ancla.ArribaCentro, 0, -14, 136, 66);
                var ic = Icono(hit.transform, icono, blanco, Ancla.ArribaCentro, 0, -20, 54);
                var tx = Texto(hit.transform, "Texto", titulo, 24, Ancla.AbajoCentro, 0, 14, 300, 36);
                var badge = Img("Insignia", hit.transform, SpPil, EstiloUI.Rojo);
                Ubicar(badge.gameObject, Ancla.ArribaCentro, 40, -12, 24, 24);
                return new NavegacionUI.Pestana { nombre = titulo, boton = b, icono = ic, texto = tx, resaltado = res2, insignia = badge.gameObject, contenido = new[] { contenido } };
            }
            var navUI = canvas.gameObject.AddComponent<NavegacionUI>();
            navUI.pestanas = new[]
            {
                Tab("Guía", "chat", pGuia),
                Tab("Explorar", "camara", pExplorar),
                Tab("Clima", "nube", pClima),
            };
            navUI.colorInactivo = new Color(0.76f, 0.86f, 1f, 0.6f);
            pExplorar.SetActive(false);
            pClima.SetActive(false);

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
            ui.textoMic = textoVoz;
            ui.estadoVoz = estadoVoz.gameObject;
            ui.botonSilenciar = silenciar;
            ui.botonAcompanar = acompanar;
            ui.textoAcompanar = textoAcompanar;
            ui.botonVenAqui = venAqui;
            ui.panelInferior = (RectTransform)panel.transform;
            ui.ocultablesAlPlegar = new[] { chatGo };
            ui.botonPlegar = plegar;
            ui.iconoPlegar = chevron.rectTransform;
            ui.altoPanelAbierto = 780; ui.altoPanelPlegado = 330;

            var exp = canvas.gameObject.AddComponent<ExplorarUI>();
            exp.clasificador = clasificador;
            exp.captura = captura;
            exp.navegacion = navUI;
            exp.guia = ui;
            exp.esquinas = esquinas;
            exp.lineaEscaneo = (RectTransform)linea.transform;
            exp.altoMarco = Marco;
            exp.textoAyuda = ayuda;
            exp.aviso = aviso.gameObject;
            exp.textoAviso = textoAviso;
            exp.etiqueta = etiqueta.gameObject;
            exp.textoEtiqueta = textoEt;
            exp.miniatura = miniImg;
            exp.textoTipo = textoTipo;
            exp.textoNombre = nombre;
            exp.textoCientifico = cientifico;
            exp.barra = barra.gameObject;
            exp.rellenoBarra = rRt;
            exp.imagenRelleno = relleno;
            exp.textoConfianza = confianza;
            exp.textoDescripcion = descripcion;
            exp.botonPreguntar = preguntar;
            exp.botonVolver = volverE;

            var cli = canvas.gameObject.AddComponent<ClimaUI>();
            cli.fuente = fuenteClima;
            cli.navegacion = navUI;
            cli.guia = ui;
            cli.iconoCondicion = iconoCond;
            cli.textoTemperatura = temp;
            cli.textoCondicion = cond;
            cli.textoHumedad = hum;
            cli.textoViento = vie;
            cli.textoPresion = pre;
            cli.iconoEstado = iconoEst;
            cli.iconoEstadoBarra = antenaBarra;
            cli.textoEstado = textoEst;
            cli.textoEstadoDetalle = detalleEst;
            cli.contenedorAlertas = (RectTransform)alertas.transform;
            cli.contenedorRecomendaciones = (RectTransform)recs.transform;
            cli.botonEmergencia = sosC;
            cli.botonVolver = volverC;
            cli.spriteTarjeta = SpRed;
            cli.spritePildora = SpPil;
            cli.iconos = NombresIconos.Select(Ico).ToArray();

            EditorUtility.SetDirty(ui);
            return ui;
        }

        static Button CrearPrefabChip()
        {
            var go = new GameObject("Chip", typeof(RectTransform), typeof(Image), typeof(Button), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            var img = go.GetComponent<Image>();
            img.sprite = SpPil;
            img.type = Image.Type.Sliced;
            img.color = new Color(1f, 1f, 1f, 0.15f);
            go.GetComponent<Button>().targetGraphic = img;
            var h = go.GetComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(30, 30, 10, 10);
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childControlWidth = true; h.childControlHeight = true;
            h.childForceExpandWidth = false; h.childForceExpandHeight = false;
            var le = go.GetComponent<LayoutElement>();
            le.minHeight = 72; le.preferredHeight = 72;

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
        static ModoSinAR CrearModoSinAR(GameObject raiz, GameObject prefabFrailejon, GameObject prefabCondor, KnowledgeBase kb, Texture2D[] fotos)
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
            modo.frailejonFijo = fijo;
            modo.prefabCondor = prefabCondor;
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

            // Solo el Input System nuevo: 'Both' no esta soportado en Android (Unity 6.6).
            // La inclinacion usa Accelerometer (Input System) y el GPS usa Input.location, que no depende de esto.
            var ajustes = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset").FirstOrDefault();
            if (ajustes != null)
            {
                var so = new SerializedObject(ajustes);
                var p = so.FindProperty("activeInputHandler");
                if (p != null && p.intValue != 1)
                {
                    p.intValue = 1;   // 0 = clasico, 1 = nuevo (Input System), 2 = ambos
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
