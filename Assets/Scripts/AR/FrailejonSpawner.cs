using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace GuiaFrailejon
{
    /// <summary>
    /// AR con marcador: cuando la camara ve el letrero de una estacion, aparece el frailejon encima.
    /// Requiere AR Foundation 6.x (evento trackablesChanged). Los nombres de las imagenes en la
    /// Reference Image Library deben coincidir con "marcador" en base_conocimiento.json (estacion_1, ...).
    /// </summary>
    [RequireComponent(typeof(ARTrackedImageManager))]
    public class FrailejonSpawner : MonoBehaviour
    {
        public GameObject prefabFrailejon;
        public KnowledgeBase kb;
        [Tooltip("Desplazamiento sobre el letrero (metros).")]
        public Vector3 desplazamiento = new Vector3(0, 0.05f, 0);
        public float escala = 0.4f;
        [Header("Condor")]
        public GameObject prefabCondor;
        [Tooltip("1 = tamano real del modelo (el prefab ya viene en metros). Ajustarlo aqui si queda muy grande en el letrero.")]
        public float escalaCondor = 1f;
        [Tooltip("Opcional: texto en pantalla que muestra que marcador ve la camara (para pruebas).")]
        public TMP_Text textoDebug;
        [Tooltip("SOLO PRUEBAS: pone un cubo rosado de 10 cm sobre el letrero para ver si la deteccion funciona.")]
        public bool cuboDePrueba = false;
        [Tooltip("Muestra el texto amarillo de diagnostico. Desactivarlo para la demo.")]
        public bool mostrarDiagnostico = true;
        [Tooltip("Opcional: modo en que Frai acompana al visitante.")]
        public FraiAcompanante acompanante;

        /// <summary>Posicion del ultimo letrero visto (para que el acompanante salga de ahi).</summary>
        public Vector3? UltimaPosicion { get; private set; }

        public event Action<Estacion, GameObject> AlDetectarEstacion;
        public Estacion EstacionActual { get; private set; }

        [Tooltip("Libreria de letreros. Se asigna aqui tambien por seguridad: si el ARTrackedImageManager la pierde, se le vuelve a poner al iniciar.")]
        public XRReferenceImageLibrary libreria;

        ARTrackedImageManager gestor;
        readonly Dictionary<string, GameObject> instancias = new Dictionary<string, GameObject>();
        bool algunoDetectado;
        string infoFrai = "";

        void Awake() => gestor = GetComponent<ARTrackedImageManager>();

        System.Collections.IEnumerator Start()
        {
            // Espera a que la sesion AR arranque y asegura que la libreria este cargada.
            for (int i = 0; i < 20 && ARSession.state < ARSessionState.SessionInitializing; i++)
                yield return new WaitForSeconds(0.25f);

            if ((gestor.referenceLibrary == null || gestor.referenceLibrary.count == 0) && libreria != null)
            {
                try { gestor.referenceLibrary = libreria; Debug.Log("[AR] Libreria asignada desde FrailejonSpawner."); }
                catch (Exception e) { Debug.LogError("[AR] No se pudo asignar la libreria: " + e.Message); }
            }

            // Mientras no se detecte ningun letrero, muestra el estado cada segundo.
            while (!algunoDetectado)
            {
                var lib = gestor.referenceLibrary;
                Debug_($"Buscando letrero... AR: {ARSession.state} | letreros: {(lib != null ? lib.count.ToString() : "SIN LIBRERIA")}");
                yield return new WaitForSeconds(1f);
            }
        }

        string ultimoLog;
        void Debug_(string t)
        {
            if (textoDebug) { textoDebug.gameObject.SetActive(mostrarDiagnostico); textoDebug.text = t; }
            if (t != ultimoLog) { ultimoLog = t; Debug.Log("[AR] " + t); }
        }
        void OnEnable() { gestor.trackablesChanged.AddListener(AlCambiar); AvatarSeleccion.Cambio += AlCambiarAvatar; }
        void OnDisable() { gestor.trackablesChanged.RemoveListener(AlCambiar); AvatarSeleccion.Cambio -= AlCambiarAvatar; }

        bool EsCondor => AvatarSeleccion.Actual == Avatar.Condor && prefabCondor != null;

        /// <summary>Al cambiar de personaje se borran los actuales; reaparecen con el nuevo en el siguiente cuadro del letrero.</summary>
        void AlCambiarAvatar()
        {
            foreach (var go in instancias.Values) if (go) Destroy(go);
            instancias.Clear();
        }

        void AlCambiar(ARTrackablesChangedEventArgs<ARTrackedImage> args)
        {
            foreach (var img in args.added) Procesar(img);
            foreach (var img in args.updated) Procesar(img);
        }

        void Procesar(ARTrackedImage img)
        {
            string marcador = img.referenceImage.name;
            // Se muestra tambien en estado Limited: en pantallas y con poca luz ARCore suele reportar Limited.
            bool visible = img.trackingState != TrackingState.None;
            algunoDetectado = true;
            Debug_($"Letrero: {marcador} ({img.trackingState}){(string.IsNullOrEmpty(infoFrai) ? "" : "  " + infoFrai)}");
            if (visible) UltimaPosicion = img.transform.position;

            var est = kb != null ? kb.BuscarEstacionPorMarcador(marcador) : null;
            bool estacionNueva = visible && est != null && est != EstacionActual;

            // Modo acompanante: no se pone otro Frai en el letrero; el acompanante "salta" hasta aqui.
            if (acompanante != null && acompanante.Activo)
            {
                if (instancias.TryGetValue(marcador, out var oculto)) oculto.SetActive(false);
                if (estacionNueva) acompanante.Llamar(img.transform.position);
            }
            else
            {
                if (!instancias.TryGetValue(marcador, out var go))
                {
                    if (!visible) return;
                    var prefab = EsCondor ? prefabCondor : prefabFrailejon;
                    if (prefab == null) { Debug_("ERROR: Frailejon Spawner no tiene Prefab Frailejon"); return; }
                    go = Instantiate(prefab, img.transform);
                    go.transform.localPosition = desplazamiento;
                    go.transform.localScale = Vector3.one * (EsCondor ? escalaCondor : escala);
                    instancias[marcador] = go;

                    if (cuboDePrueba)
                    {
                        var cubo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        Destroy(cubo.GetComponent<Collider>());
                        cubo.transform.SetParent(img.transform, false);
                        cubo.transform.localScale = Vector3.one * 0.1f;
                        cubo.transform.localPosition = new Vector3(0, 0.05f, 0);
                    }

                    var rs = go.GetComponentsInChildren<Renderer>(true);
                    Bounds b = rs.Length > 0 ? rs[0].bounds : new Bounds(go.transform.position, Vector3.zero);
                    foreach (var r in rs) b.Encapsulate(r.bounds);
                    float dist = Camera.main ? Vector3.Distance(Camera.main.transform.position, b.center) : -1;
                    infoFrai = $"Frai: {rs.Length} piezas, {b.size.y:0.00} m, a {dist:0.00} m";
                }
                go.SetActive(visible);
                if (visible) MirarCamara(go.transform);
            }

            if (estacionNueva)
            {
                EstacionActual = est;
                instancias.TryGetValue(marcador, out var actualGo);
                AlDetectarEstacion?.Invoke(est, actualGo);
            }
        }

        /// <summary>Oculta los Frai pegados a los letreros (al pasar a modo acompanante).</summary>
        public void OcultarMarcadores()
        {
            foreach (var go in instancias.Values) if (go) go.SetActive(false);
        }

        static void MirarCamara(Transform t)
        {
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 dir = cam.transform.position - t.position;
            dir.y = 0;   // solo gira en el eje vertical, no se inclina
            if (dir.sqrMagnitude > 0.0001f) t.rotation = Quaternion.LookRotation(dir);
        }
    }
}
