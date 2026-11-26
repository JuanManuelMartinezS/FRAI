using System;
using System.Collections;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

namespace GuiaFrailejon
{
    /// <summary>Parametros de la licencia de visita. Se editan en StreamingAssets/config_visita.json (antes de compilar).</summary>
    [Serializable]
    public class ConfigVisita
    {
        public double lat = 5.065556;          // centro del area permitida (grados decimales)
        public double lon = -75.504722;
        public float radio_m = 5000;           // radio del area permitida
        public float horas = 24;               // duracion de la licencia desde que el visitante entra al area
        public int lecturas_fuera = 3;         // lecturas GPS consecutivas fuera del area antes de bloquear
        public float precision_max_m = 100;    // lecturas GPS peores que esto se ignoran
        public float intervalo_s = 10;         // cada cuanto se revisa
        public int dias_max_sin_activar = 7;   // si nunca entra al area, la app caduca igual tras estos dias
    }

    /// <summary>
    /// Limita el uso de la app a la visita al parque:
    ///  - La licencia se ACTIVA la primera vez que el GPS ve al visitante dentro del area (antes puede preparar el viaje).
    ///  - Se BLOQUEA cuando: pasan las horas de la licencia, el GPS lo ve fuera del area varias veces seguidas,
    ///    la hora del celular se retrocede, o pasan dias_max_sin_activar sin haber entrado nunca.
    ///  - Al bloquear: borra los datos descargados/guardados, desactiva la app y muestra una pantalla fija
    ///    con boton para desinstalar. Es persistente: reabrir la app vuelve a mostrar el bloqueo.
    /// Se crea solo al iniciar la escena (no hay que agregarlo a ninguna escena). No corre en el Editor.
    /// Sin GPS (sin fix / sin permiso) NO bloquea por ubicacion: manda el tiempo.
    /// </summary>
    public class LicenciaVisita : MonoBehaviour
    {
        const string KInstalado = "lv_instalado", KActivada = "lv_activada", KUltimo = "lv_ultimo", KBloqueada = "lv_bloqueada", KMotivo = "lv_motivo";
        public static ConfigVisita Config { get; private set; } = new ConfigVisita();
        public static bool Bloqueada { get; private set; }

        int fuera;
        bool gpsIniciado;

#if !UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Iniciar()
        {
            var go = new GameObject("LicenciaVisita");
            DontDestroyOnLoad(go);
            go.AddComponent<LicenciaVisita>();
        }
#endif

        IEnumerator Start()
        {
            yield return CargarConfig();

            if (!PlayerPrefs.HasKey(KInstalado)) Guardar(KInstalado, DateTime.UtcNow);
            if (PlayerPrefs.GetInt(KBloqueada, 0) == 1) { Bloquear(PlayerPrefs.GetString(KMotivo, "Tu visita terminó."), false); yield break; }

#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation)) Permission.RequestUserPermission(Permission.FineLocation);
#endif
            while (!Bloqueada)
            {
                Evaluar();
                yield return new WaitForSecondsRealtime(Mathf.Max(2f, Config.intervalo_s));
            }
        }

        void OnApplicationFocus(bool enFoco) { if (enFoco && !Bloqueada && PlayerPrefs.HasKey(KInstalado)) Evaluar(); }

        IEnumerator CargarConfig()
        {
            string ruta = Path.Combine(Application.streamingAssetsPath, "config_visita.json");
            if (!ruta.Contains("://")) ruta = "file://" + ruta;
            using (var req = UnityWebRequest.Get(ruta))
            {
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success)
                {
                    try { Config = JsonUtility.FromJson<ConfigVisita>(req.downloadHandler.text) ?? Config; }
                    catch (Exception e) { Debug.LogWarning("[Licencia] config_visita.json invalido, uso valores por defecto: " + e.Message); }
                }
                else Debug.LogWarning("[Licencia] Sin config_visita.json, uso valores por defecto.");
            }
        }

        // ---------------- Evaluacion ----------------

        void Evaluar()
        {
            if (Bloqueada) return;
            var ahora = DateTime.UtcNow;

            // Reloj retrocedido: se intento estirar la licencia.
            if (Leer(KUltimo, out var ultimo))
            {
                if (ahora < ultimo.AddMinutes(-5)) { Bloquear("La hora del dispositivo fue modificada."); return; }
                if (ahora > ultimo) Guardar(KUltimo, ahora);
            }
            else Guardar(KUltimo, ahora);

            bool activada = Leer(KActivada, out var inicio);
            if (activada && (ahora - inicio).TotalHours >= Config.horas) { Bloquear("Tu visita de " + Config.horas + " horas terminó."); return; }
            if (!activada && Leer(KInstalado, out var inst) && (ahora - inst).TotalDays >= Config.dias_max_sin_activar)
            { Bloquear("La app caducó."); return; }

            if (!LeerGps(out double lat, out double lon, out float prec) || prec > Config.precision_max_m) return;   // sin fix: manda el tiempo
            bool dentro = Distancia(lat, lon, Config.lat, Config.lon) <= Config.radio_m;

            if (!activada) { if (dentro) { Guardar(KActivada, ahora); Debug.Log("[Licencia] Visita activada."); } return; }
            fuera = dentro ? 0 : fuera + 1;
            if (fuera >= Config.lecturas_fuera) Bloquear("Saliste del parque. La información de la guía solo está disponible durante tu visita.");
        }

        bool LeerGps(out double lat, out double lon, out float prec)
        {
            lat = lon = 0; prec = float.MaxValue;
            try
            {
                if (!gpsIniciado && Input.location.isEnabledByUser) { Input.location.Start(10f, 5f); gpsIniciado = true; }
                if (Input.location.status != LocationServiceStatus.Running) return false;
                var d = Input.location.lastData;
                lat = d.latitude; lon = d.longitude; prec = d.horizontalAccuracy;
                return Math.Abs(lat) > 0.0001 || Math.Abs(lon) > 0.0001;
            }
            catch (Exception) { return false; }   // Input.location puede no estar disponible con el Input System nuevo
        }

        static float Distancia(double lat1, double lon1, double lat2, double lon2) => GeoLocalizador.Distancia(lat1, lon1, lat2, lon2);

        // ---------------- Bloqueo ----------------

        void Bloquear(string motivo, bool borrar = true)
        {
            if (Bloqueada) return;
            Bloqueada = true;
            Debug.Log("[Licencia] BLOQUEADA: " + motivo);

            if (borrar) BorrarDatos();
            PlayerPrefs.SetInt(KBloqueada, 1);
            PlayerPrefs.SetString(KMotivo, motivo);
            PlayerPrefs.Save();

            // Apaga el resto de la app (IA, voz, camara, GPS de estaciones...).
            foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (mb != null && mb != this && mb.GetType().Namespace == "GuiaFrailejon") mb.enabled = false;
            try { Microphone.End(null); } catch (Exception) { }
            AudioListener.pause = true;
            try { if (gpsIniciado) Input.location.Stop(); } catch (Exception) { }

            CrearPantalla(motivo);
        }

        /// <summary>Borra todo lo que la app guardo en el dispositivo (base descargada, modelos descargados, fotos, preferencias).
        /// Lo que viene empaquetado dentro del APK/IPA no se puede borrar desde la app: por eso se bloquea y se ofrece desinstalar.</summary>
        static void BorrarDatos()
        {
            string raiz = Application.persistentDataPath;
            try
            {
                foreach (var f in Directory.GetFiles(raiz)) try { File.Delete(f); } catch (Exception) { }
                foreach (var d in Directory.GetDirectories(raiz)) try { Directory.Delete(d, true); } catch (Exception) { }
            }
            catch (Exception e) { Debug.LogWarning("[Licencia] Borrado parcial: " + e.Message); }

            // Se conservan solo las claves de la licencia.
            string inst = PlayerPrefs.GetString(KInstalado, ""), act = PlayerPrefs.GetString(KActivada, ""), ult = PlayerPrefs.GetString(KUltimo, "");
            PlayerPrefs.DeleteAll();
            if (inst != "") PlayerPrefs.SetString(KInstalado, inst);
            if (act != "") PlayerPrefs.SetString(KActivada, act);
            if (ult != "") PlayerPrefs.SetString(KUltimo, ult);
        }

        // ---------------- Pantalla de bloqueo ----------------

        void CrearPantalla(string motivo)
        {
            var cgo = new GameObject("PantallaBloqueo", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            cgo.transform.SetParent(transform, false);
            var canvas = cgo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;
            var sc = cgo.GetComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1080, 1920);
            sc.matchWidthOrHeight = 0.5f;

            var fondo = Nuevo("Fondo", cgo.transform, typeof(Image)).GetComponent<Image>();
            fondo.color = new Color(0.05f, 0.09f, 0.07f, 1f);
            Estirar(fondo.rectTransform);

            Texto(cgo.transform, "Gracias por visitarnos", 72, new Vector2(0, 380), new Vector2(900, 200), FontStyles.Bold);
            Texto(cgo.transform, motivo + "\n\nLa guía ya no está disponible y se borró la información guardada en este dispositivo.", 46,
                  new Vector2(0, 0), new Vector2(900, 600), FontStyles.Normal);

#if UNITY_ANDROID
            var b = Nuevo("BotonDesinstalar", cgo.transform, typeof(Image), typeof(Button));
            var rt = (RectTransform)b.transform;
            rt.sizeDelta = new Vector2(760, 150); rt.anchoredPosition = new Vector2(0, -520);
            b.GetComponent<Image>().color = new Color(0.78f, 0.22f, 0.18f);
            b.GetComponent<Button>().onClick.AddListener(Desinstalar);
            Texto(b.transform, "Desinstalar la app", 52, Vector2.zero, new Vector2(760, 150), FontStyles.Bold);
#endif
        }

        static GameObject Nuevo(string nombre, Transform padre, params Type[] comps)
        {
            var go = new GameObject(nombre, typeof(RectTransform));
            foreach (var c in comps) go.AddComponent(c);
            go.transform.SetParent(padre, false);
            return go;
        }

        static void Estirar(RectTransform rt) { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero; }

        static void Texto(Transform padre, string txt, float tam, Vector2 pos, Vector2 size, FontStyles estilo)
        {
            var go = Nuevo("Texto", padre, typeof(TextMeshProUGUI));
            var t = go.GetComponent<TextMeshProUGUI>();
            t.text = txt; t.fontSize = tam; t.fontStyle = estilo; t.alignment = TextAlignmentOptions.Center; t.color = Color.white;
            t.raycastTarget = false;
            var rt = t.rectTransform; rt.anchoredPosition = pos; rt.sizeDelta = size;
        }

        static void Desinstalar()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var up = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var act = up.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var uriCls = new AndroidJavaClass("android.net.Uri"))
                using (var uri = uriCls.CallStatic<AndroidJavaObject>("parse", "package:" + Application.identifier))
                using (var intent = new AndroidJavaObject("android.content.Intent", "android.intent.action.DELETE", uri))
                    act.Call("startActivity", intent);
            }
            catch (Exception e) { Debug.LogWarning("[Licencia] No se pudo abrir el desinstalador: " + e.Message); }
#endif
        }

        // ---------------- Persistencia de fechas ----------------

        static void Guardar(string clave, DateTime utc) { PlayerPrefs.SetString(clave, utc.Ticks.ToString()); PlayerPrefs.Save(); }

        static bool Leer(string clave, out DateTime utc)
        {
            utc = default;
            if (!long.TryParse(PlayerPrefs.GetString(clave, ""), out long t) || t <= 0) return false;
            utc = new DateTime(t, DateTimeKind.Utc);
            return true;
        }
    }
}
