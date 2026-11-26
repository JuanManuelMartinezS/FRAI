using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UI;

namespace GuiaFrailejon
{
    /// <summary>
    /// Pestana "Recursos": muestra cuanto consume la app en el celular (FPS, CPU, memoria, graficos, bateria).
    /// Solo mide mientras la pestana esta visible, asi que no gasta nada el resto del tiempo.
    /// Las tarjetas se crean por codigo a partir de contenedorLista.
    /// </summary>
    public class RecursosUI : MonoBehaviour
    {
        [Header("Logica")]
        public NavegacionUI navegacion;

        [Header("Vista")]
        public RectTransform contenedorLista;
        public Button botonVolver;
        [Tooltip("Botoncito de la barra inferior que abre esta pantalla.")]
        public Button botonAbrir;
        public Sprite spriteTarjeta;
        public Sprite spritePildora;
        [Tooltip("Cada cuantos segundos se actualizan los numeros.")]
        public float intervalo = 1f;

        static readonly Color Fondo = new Color(0.035f, 0.10f, 0.22f, 0.86f);
        static readonly Color Suave = new Color(0.76f, 0.86f, 1f, 0.75f);
        static readonly Color Verde = new Color(0.22f, 0.78f, 0.42f);
        static readonly Color Ambar = new Color(1f, 0.79f, 0.16f);
        static readonly Color Rojo = new Color(0.90f, 0.20f, 0.22f);

        class Fila { public TMP_Text valor, detalle; public Image relleno; }

        Fila fps, cpu, ram, sistema, graficos, bateria;
        TMP_Text infoDispositivo;
        bool activa;

        // Medicion de FPS
        float acumTiempo, peorCuadro; int cuadros;
        float proximo;

        // Medicion de CPU (Android: /proc/self/stat)
        double cpuTicksPrev; float cpuTiempoPrev = -1;

#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidJavaObject actividad, gestorMemoria;
#endif

        void Start()
        {
            Construir();
            if (botonVolver) botonVolver.onClick.AddListener(() => navegacion.Ir(NavegacionUI.Guia));
            if (botonAbrir) botonAbrir.onClick.AddListener(() => navegacion.Ir(NavegacionUI.Recursos));
            if (navegacion) navegacion.AlCambiar += i =>
            {
                activa = i == NavegacionUI.Recursos;
                if (activa) { Reiniciar(); Medir(); }
            };
        }

        void Reiniciar() { acumTiempo = 0; cuadros = 0; peorCuadro = 0; cpuTiempoPrev = -1; proximo = Time.unscaledTime + intervalo; }

        void Update()
        {
            if (!activa) return;
            float dt = Time.unscaledDeltaTime;
            acumTiempo += dt; cuadros++;
            if (dt > peorCuadro) peorCuadro = dt;
            if (Time.unscaledTime >= proximo) { proximo = Time.unscaledTime + intervalo; Medir(); }
        }

        // ------------------------------------------------------------------
        // Mediciones
        // ------------------------------------------------------------------
        void Medir()
        {
            // ---- FPS ----
            if (cuadros > 0 && acumTiempo > 0)
            {
                float f = cuadros / acumTiempo;
                float meta = Application.targetFrameRate > 0 ? Application.targetFrameRate : 60f;
                Poner(fps, $"{f:0} FPS", $"Cuadro más lento: {peorCuadro * 1000f:0} ms · meta {meta:0}", Mathf.Clamp01(f / meta),
                      f >= meta * 0.8f ? Verde : f >= meta * 0.5f ? Ambar : Rojo);
            }
            acumTiempo = 0; cuadros = 0; peorCuadro = 0;

            // ---- CPU ----
            float? uso = UsoCpu();
            if (uso.HasValue)
                Poner(cpu, $"{uso.Value * 100f:0} %", $"{SystemInfo.processorCount} núcleos · {SystemInfo.processorFrequency} MHz",
                      uso.Value, uso.Value < 0.5f ? Verde : uso.Value < 0.8f ? Ambar : Rojo);
            else
                Poner(cpu, "No disponible", $"{SystemInfo.processorCount} núcleos · {SystemInfo.processorFrequency} MHz", 0, Suave);

            // ---- Memoria de la app ----
            float totalMB = SystemInfo.systemMemorySize;
            float appMB = MemoriaAppMB();
            float frac = totalMB > 0 ? Mathf.Clamp01(appMB / totalMB) : 0;
            float mono = Profiler.GetMonoUsedSizeLong() / 1048576f;
            float unity = Profiler.GetTotalAllocatedMemoryLong() / 1048576f;
            Poner(ram, $"{appMB:0} MB", $"Unity {unity:0} MB · código (mono) {mono:0} MB", frac,
                  frac < 0.25f ? Verde : frac < 0.4f ? Ambar : Rojo);

            // ---- Memoria del celular ----
            if (MemoriaSistemaMB(out float libre, out float total))
            {
                float usada = total - libre;
                float fu = total > 0 ? Mathf.Clamp01(usada / total) : 0;
                Poner(sistema, $"{usada:0} / {total:0} MB", $"Libre: {libre:0} MB (todas las apps)", fu,
                      fu < 0.7f ? Verde : fu < 0.88f ? Ambar : Rojo);
            }
            else Poner(sistema, $"{totalMB:0} MB", "Memoria total del celular", 0, Suave);

            // ---- Graficos ----
            float gpuMB = Profiler.GetAllocatedMemoryForGraphicsDriver() / 1048576f;
            float gpuTotal = SystemInfo.graphicsMemorySize;
            Poner(graficos, gpuMB > 0 ? $"{gpuMB:0} MB" : "--", $"{SystemInfo.graphicsDeviceName}",
                  gpuTotal > 0 ? Mathf.Clamp01(gpuMB / gpuTotal) : 0, Verde);

            // ---- Bateria ----
            float nivel = SystemInfo.batteryLevel;
            string estado = SystemInfo.batteryStatus switch
            {
                BatteryStatus.Charging => "cargando",
                BatteryStatus.Full => "completa",
                BatteryStatus.Discharging => "en uso",
                BatteryStatus.NotCharging => "sin cargar",
                _ => "estado desconocido",
            };
            float? t = TemperaturaBateria();
            string detalle = estado + (t.HasValue ? $" · {t.Value:0.0} °C" : "");
            if (nivel >= 0)
                Poner(bateria, $"{nivel * 100f:0} %", detalle, nivel,
                      nivel > 0.4f ? Verde : nivel > 0.2f ? Ambar : Rojo);
            else Poner(bateria, "--", "Batería no disponible", 0, Suave);
            if (t.HasValue && t.Value >= 42f && bateria.valor) bateria.detalle.color = Rojo;
            else if (bateria.detalle) bateria.detalle.color = Suave;
        }

        float? UsoCpu()
        {
            try
            {
                if (!File.Exists("/proc/self/stat")) return null;
                string s = File.ReadAllText("/proc/self/stat");
                var p = s.Substring(s.LastIndexOf(')') + 2).Split(' ');
                double ticks = double.Parse(p[11]) + double.Parse(p[12]);   // utime + stime, en 1/100 s
                float ahora = Time.realtimeSinceStartup;
                float? r = null;
                if (cpuTiempoPrev >= 0 && ahora > cpuTiempoPrev)
                {
                    double seg = (ticks - cpuTicksPrev) / 100.0;
                    r = Mathf.Clamp01((float)(seg / ((ahora - cpuTiempoPrev) * Mathf.Max(1, SystemInfo.processorCount))));
                }
                cpuTicksPrev = ticks; cpuTiempoPrev = ahora;
                return r;
            }
            catch { return null; }
        }

        float MemoriaAppMB()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                var gm = GestorMemoria();
                if (gm != null)
                {
                    using (var proc = new AndroidJavaClass("android.os.Process"))
                    {
                        int pid = proc.CallStatic<int>("myPid");
                        var infos = gm.Call<AndroidJavaObject[]>("getProcessMemoryInfo", new object[] { new int[] { pid } });
                        if (infos != null && infos.Length > 0) return infos[0].Call<int>("getTotalPss") / 1024f;   // KB -> MB
                    }
                }
            }
            catch { }
#endif
            return Profiler.GetTotalReservedMemoryLong() / 1048576f;
        }

        bool MemoriaSistemaMB(out float libreMB, out float totalMB)
        {
            libreMB = totalMB = 0;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                var gm = GestorMemoria();
                if (gm != null)
                {
                    using (var mi = new AndroidJavaObject("android.app.ActivityManager$MemoryInfo"))
                    {
                        gm.Call("getMemoryInfo", mi);
                        libreMB = mi.Get<long>("availMem") / 1048576f;
                        totalMB = mi.Get<long>("totalMem") / 1048576f;
                        return totalMB > 0;
                    }
                }
            }
            catch { }
#endif
            return false;
        }

        float? TemperaturaBateria()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                var act = Actividad();
                using (var filtro = new AndroidJavaObject("android.content.IntentFilter", "android.intent.action.BATTERY_CHANGED"))
                using (var intent = act.Call<AndroidJavaObject>("registerReceiver", null, filtro))
                {
                    if (intent != null)
                    {
                        int dec = intent.Call<int>("getIntExtra", "temperature", int.MinValue);   // decimas de grado
                        if (dec != int.MinValue) return dec / 10f;
                    }
                }
            }
            catch { }
#endif
            return null;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidJavaObject Actividad()
        {
            if (actividad == null)
                using (var u = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                    actividad = u.GetStatic<AndroidJavaObject>("currentActivity");
            return actividad;
        }

        AndroidJavaObject GestorMemoria()
        {
            if (gestorMemoria == null) gestorMemoria = Actividad().Call<AndroidJavaObject>("getSystemService", "activity");
            return gestorMemoria;
        }
#endif

        // ------------------------------------------------------------------
        // Tarjetas
        // ------------------------------------------------------------------
        void Construir()
        {
            if (!contenedorLista) return;
            fps = Tarjeta("Rendimiento", "Fotogramas por segundo");
            cpu = Tarjeta("Procesador", "Uso de CPU de la app");
            ram = Tarjeta("Memoria de la app", "RAM que ocupa Frai");
            sistema = Tarjeta("Memoria del celular", "RAM en uso por todo el sistema");
            graficos = Tarjeta("Gráficos", "Memoria de video");
            bateria = Tarjeta("Batería", "Nivel y temperatura");

            var cab = Texto(contenedorLista, "Dispositivo", 22, Suave, FontStyles.Normal, TextAlignmentOptions.Center);
            cab.margin = new Vector4(24, 6, 24, 0);
            cab.text = $"{SystemInfo.deviceModel}\n{SystemInfo.operatingSystem}\n" +
                       $"Modo de la guía: {DeviceTier.Detectar(true)}" +
                       "  ·  los valores se actualizan cada segundo mientras esta pestaña está abierta.";
            infoDispositivo = cab;
        }

        Fila Tarjeta(string titulo, string subtitulo)
        {
            var go = new GameObject("Recurso_" + titulo, typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            go.transform.SetParent(contenedorLista, false);
            var img = go.GetComponent<Image>();
            img.sprite = spriteTarjeta; img.type = Image.Type.Sliced; img.color = Fondo; img.raycastTarget = false;
            var v = go.GetComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(32, 32, 24, 26); v.spacing = 10;
            v.childControlWidth = true; v.childControlHeight = true;
            v.childForceExpandWidth = true; v.childForceExpandHeight = false;

            // Titulo + valor en una fila
            var fila = new GameObject("Fila", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            fila.transform.SetParent(go.transform, false);
            var h = fila.GetComponent<HorizontalLayoutGroup>();
            h.childControlWidth = true; h.childControlHeight = true;
            h.childForceExpandWidth = false; h.childForceExpandHeight = false;
            h.childAlignment = TextAnchor.MiddleLeft;
            var t = Texto(fila.transform, titulo, 30, Color.white, FontStyles.Bold, TextAlignmentOptions.Left);
            t.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            var valor = Texto(fila.transform, "--", 34, Color.white, FontStyles.Bold, TextAlignmentOptions.Right);
            valor.textWrappingMode = TextWrappingModes.NoWrap;

            // Barra
            var fondo = new GameObject("Barra", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            fondo.transform.SetParent(go.transform, false);
            var fi = fondo.GetComponent<Image>();
            fi.sprite = spritePildora; fi.type = Image.Type.Sliced; fi.color = new Color(1, 1, 1, 0.14f); fi.raycastTarget = false;
            var le = fondo.GetComponent<LayoutElement>(); le.minHeight = le.preferredHeight = 18;
            var rel = new GameObject("Relleno", typeof(RectTransform), typeof(Image));
            rel.transform.SetParent(fondo.transform, false);
            var ri = rel.GetComponent<Image>();
            ri.sprite = spritePildora; ri.type = Image.Type.Sliced; ri.color = Verde; ri.raycastTarget = false;
            var rt = (RectTransform)rel.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = new Vector2(0.02f, 1); rt.offsetMin = rt.offsetMax = Vector2.zero;

            var det = Texto(go.transform, subtitulo, 24, Suave, FontStyles.Normal, TextAlignmentOptions.Left);
            return new Fila { valor = valor, detalle = det, relleno = ri };
        }

        static void Poner(Fila f, string valor, string detalle, float fraccion, Color color)
        {
            if (f == null) return;
            f.valor.text = valor;
            f.detalle.text = detalle;
            f.relleno.color = color;
            var rt = (RectTransform)f.relleno.transform;
            rt.anchorMax = new Vector2(Mathf.Clamp(fraccion, 0.02f, 1f), 1);
        }

        static TMP_Text Texto(Transform padre, string texto, float tam, Color color, FontStyles estilo, TextAlignmentOptions alin)
        {
            var go = new GameObject("Texto", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(padre, false);
            var t = go.GetComponent<TextMeshProUGUI>();
            t.text = texto; t.fontSize = tam; t.color = color; t.fontStyle = estilo; t.alignment = alin;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.raycastTarget = false;
            return t;
        }
    }
}
