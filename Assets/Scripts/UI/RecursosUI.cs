using System;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UI;

namespace GuiaFrailejon
{
    /// <summary>
    /// Pestana "Recursos": consumo de la app en el celular.
    ///  - Ahora: FPS, CPU, memoria, graficos y bateria (se actualiza cada segundo mientras la pestana esta abierta).
    ///  - Estadisticas: historial de los ultimos 5 min, resumen de la sesion y acumulado entre sesiones.
    /// Las estadisticas se muestrean siempre, cada 5 s (es muy barato), aunque la pestana este cerrada.
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
        [Tooltip("Cada cuantos segundos se actualizan los numeros 'en vivo'.")]
        public float intervalo = 1f;
        [Tooltip("Cada cuantos segundos se toma una muestra para las estadisticas.")]
        public float intervaloEstadisticas = 5f;

        const int Muestras = 60;   // 60 muestras x 5 s = 5 min de historial

        static readonly Color Fondo = new Color(0.035f, 0.10f, 0.22f, 0.86f);
        static readonly Color Suave = new Color(0.76f, 0.86f, 1f, 0.75f);
        static readonly Color Verde = new Color(0.22f, 0.78f, 0.42f);
        static readonly Color Ambar = new Color(1f, 0.79f, 0.16f);
        static readonly Color Rojo = new Color(0.90f, 0.20f, 0.22f);
        static readonly Color Azul = new Color(0.16f, 0.52f, 1f);

        class Fila { public TMP_Text valor, detalle; public Image relleno; }
        class Grafica { public Image[] barras; public TMP_Text valor; public Color color; public float[] datos = new float[Muestras]; }

        Fila fps, cpu, ram, sistema, graficos, bateria;
        Grafica gFps, gCpu, gRam;
        TMP_Text txTiempo, txFps, txCpu, txRam, txTemp, txBateria;
        TMP_Text txSesiones, txTiempoTotal, txConsumoMedio, txAutonomia;
        bool activa;

        // ---- Medicion "en vivo" ----
        float acumTiempo, peorCuadro; int cuadros;
        float proximo;
        double cpuTicksLive; float cpuTiempoLive = -1;

        // ---- Muestreo para estadisticas ----
        float sAcumTiempo, sPeor; int sCuadros;
        float proximoStat;
        double cpuTicksStat; float cpuTiempoStat = -1;
        float inicio;

        // Resumen de la sesion
        double fpsSuma, cpuSuma, ramSuma; int fpsN, cpuN, ramN;
        float fpsMin = float.MaxValue, cpuPico, ramPico, tempPico = float.MinValue;
        float nivelInicial = -1, nivelPrevio = -1, nivelActual = -1;
        float drenado, segDescarga;   // drenado en fraccion (0..1); segundos descargando

        // Acumulado de sesiones anteriores
        int sesionesPrev; float segPrev, drenadoPrev, segDescargaPrev;
        float proximoGuardado;
        const string K = "recursos_";

#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidJavaObject actividad, gestorMemoria;
#endif

        void Start()
        {
            inicio = Time.realtimeSinceStartup;
            sesionesPrev = PlayerPrefs.GetInt(K + "sesiones", 0);
            segPrev = PlayerPrefs.GetFloat(K + "seg", 0);
            drenadoPrev = PlayerPrefs.GetFloat(K + "drenado", 0);
            segDescargaPrev = PlayerPrefs.GetFloat(K + "segDescarga", 0);
            PlayerPrefs.SetInt(K + "sesiones", sesionesPrev + 1);   // cuenta esta sesion

            Construir();
            proximoStat = Time.unscaledTime + intervaloEstadisticas;
            proximoGuardado = Time.unscaledTime + 60f;

            if (botonVolver) botonVolver.onClick.AddListener(() => navegacion.Ir(NavegacionUI.Guia));
            if (botonAbrir) botonAbrir.onClick.AddListener(() => navegacion.Ir(NavegacionUI.Recursos));
            if (navegacion) navegacion.AlCambiar += i =>
            {
                activa = i == NavegacionUI.Recursos;
                if (activa) { Reiniciar(); Medir(); PintarEstadisticas(); }
            };
        }

        void Reiniciar() { acumTiempo = 0; cuadros = 0; peorCuadro = 0; cpuTiempoLive = -1; proximo = Time.unscaledTime + intervalo; }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            // Ventana para estadisticas (siempre)
            sAcumTiempo += dt; sCuadros++;
            if (dt > sPeor) sPeor = dt;
            if (Time.unscaledTime >= proximoStat) { proximoStat = Time.unscaledTime + intervaloEstadisticas; Muestrear(); }

            if (Time.unscaledTime >= proximoGuardado) { proximoGuardado = Time.unscaledTime + 60f; Guardar(); }

            // Ventana "en vivo" (solo con la pestana abierta)
            if (!activa) return;
            acumTiempo += dt; cuadros++;
            if (dt > peorCuadro) peorCuadro = dt;
            if (Time.unscaledTime >= proximo) { proximo = Time.unscaledTime + intervalo; Medir(); }
        }

        void OnApplicationPause(bool pausa) { if (pausa) Guardar(); }
        void OnApplicationQuit() { Guardar(); }

        // ------------------------------------------------------------------
        // Mediciones en vivo
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
            float? uso = UsoCpu(ref cpuTicksLive, ref cpuTiempoLive);
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
            if (bateria.detalle) bateria.detalle.color = t.HasValue && t.Value >= 42f ? Rojo : Suave;
        }

        // ------------------------------------------------------------------
        // Muestreo para estadisticas (cada ~5 s, siempre)
        // ------------------------------------------------------------------
        void Muestrear()
        {
            float ventana = sAcumTiempo;
            float f = ventana > 0 ? sCuadros / ventana : 0;
            sAcumTiempo = 0; sCuadros = 0; sPeor = 0;

            if (f > 0) { fpsSuma += f; fpsN++; if (f < fpsMin) fpsMin = f; }

            float? uso = UsoCpu(ref cpuTicksStat, ref cpuTiempoStat);
            if (uso.HasValue) { cpuSuma += uso.Value; cpuN++; if (uso.Value > cpuPico) cpuPico = uso.Value; }

            float appMB = MemoriaAppMB();
            ramSuma += appMB; ramN++;
            if (appMB > ramPico) ramPico = appMB;

            float? t = TemperaturaBateria();
            if (t.HasValue && t.Value > tempPico) tempPico = t.Value;

            // Bateria: solo cuenta el consumo cuando va descargando
            float nivel = SystemInfo.batteryLevel;
            nivelActual = nivel;
            if (nivel >= 0)
            {
                if (nivelInicial < 0) nivelInicial = nivel;
                if (SystemInfo.batteryStatus == BatteryStatus.Discharging)
                {
                    segDescarga += ventana;
                    if (nivelPrevio >= 0 && nivel < nivelPrevio) drenado += nivelPrevio - nivel;
                }
                nivelPrevio = nivel;
            }

            Empujar(gFps, f);
            if (uso.HasValue) Empujar(gCpu, uso.Value); else Empujar(gCpu, 0);
            Empujar(gRam, appMB);

            if (activa) PintarEstadisticas();
        }

        static void Empujar(Grafica g, float v)
        {
            if (g == null) return;
            Array.Copy(g.datos, 1, g.datos, 0, Muestras - 1);
            g.datos[Muestras - 1] = v;
        }

        // ------------------------------------------------------------------
        // Pintado de estadisticas
        // ------------------------------------------------------------------
        void PintarEstadisticas()
        {
            float seg = Time.realtimeSinceStartup - inicio;
            if (txTiempo) txTiempo.text = Duracion(seg);
            if (txFps) txFps.text = fpsN > 0 ? $"{fpsSuma / fpsN:0} prom · {fpsMin:0} mín" : "--";
            if (txCpu) txCpu.text = cpuN > 0 ? $"{cpuSuma / cpuN * 100:0} % prom · {cpuPico * 100:0} % pico" : "No disponible";
            if (txRam) txRam.text = ramN > 0 ? $"{ramSuma / ramN:0} prom · {ramPico:0} MB pico" : "--";
            if (txTemp) txTemp.text = tempPico > float.MinValue ? $"{tempPico:0.0} °C" : "No disponible";

            if (txBateria)
            {
                if (nivelInicial < 0) txBateria.text = "No disponible";
                else
                {
                    float gastado = Mathf.Max(0, drenado) * 100f;
                    string ritmo = Ritmo(drenado, segDescarga, out float porHora) ? $" · {porHora:0.0} %/h" : "";
                    txBateria.text = $"{gastado:0.#} %{ritmo}";
                }
            }

            // Acumulado (sesiones anteriores + esta)
            if (txSesiones) txSesiones.text = (sesionesPrev + 1).ToString();
            if (txTiempoTotal) txTiempoTotal.text = Duracion(segPrev + seg);
            float dTot = drenadoPrev + drenado, sTot = segDescargaPrev + segDescarga;
            if (Ritmo(dTot, sTot, out float rTot))
            {
                if (txConsumoMedio) txConsumoMedio.text = $"{rTot:0.0} % por hora";
                if (txAutonomia) txAutonomia.text = nivelActual >= 0 ? Duracion(nivelActual * 100f / rTot * 3600f) : "--";
            }
            else
            {
                if (txConsumoMedio) txConsumoMedio.text = "Midiendo…";
                if (txAutonomia) txAutonomia.text = "Midiendo…";
            }

            // Graficas
            float meta = Application.targetFrameRate > 0 ? Application.targetFrameRate : 60f;
            DibujarGrafica(gFps, $"{gFps.datos[Muestras - 1]:0} FPS", meta);
            DibujarGrafica(gCpu, $"{gCpu.datos[Muestras - 1] * 100:0} %", 1f);
            DibujarGrafica(gRam, $"{gRam.datos[Muestras - 1]:0} MB", Mathf.Max(1f, Maximo(gRam.datos) * 1.15f));
        }

        // Ritmo de consumo en %/hora. Hace falta un minimo de datos para que no salga ruido (la bateria baja de 1 % en 1 %).
        static bool Ritmo(float drenadoFrac, float segundos, out float porHora)
        {
            porHora = 0;
            if (segundos < 180f || drenadoFrac <= 0f) return false;
            porHora = drenadoFrac * 100f / (segundos / 3600f);
            return porHora > 0.01f;
        }

        static float Maximo(float[] d) { float m = 0; foreach (var v in d) if (v > m) m = v; return m; }

        static void DibujarGrafica(Grafica g, string valor, float tope)
        {
            if (g == null || g.barras == null) return;
            if (g.valor) g.valor.text = valor;
            for (int i = 0; i < Muestras; i++)
            {
                var rt = (RectTransform)g.barras[i].transform;
                rt.anchorMax = new Vector2(rt.anchorMax.x, Mathf.Clamp(g.datos[i] / tope, 0.02f, 1f));
            }
        }

        static string Duracion(float seg)
        {
            var t = TimeSpan.FromSeconds(Mathf.Max(0, seg));
            if (t.TotalHours >= 1) return $"{(int)t.TotalHours} h {t.Minutes:00} min";
            if (t.TotalMinutes >= 1) return $"{t.Minutes} min {t.Seconds:00} s";
            return $"{t.Seconds} s";
        }

        void Guardar()
        {
            float seg = Time.realtimeSinceStartup - inicio;
            PlayerPrefs.SetFloat(K + "seg", segPrev + seg);
            PlayerPrefs.SetFloat(K + "drenado", drenadoPrev + drenado);
            PlayerPrefs.SetFloat(K + "segDescarga", segDescargaPrev + segDescarga);
            PlayerPrefs.Save();
        }

        // ------------------------------------------------------------------
        // Lecturas del sistema
        // ------------------------------------------------------------------
        static float? UsoCpu(ref double ticksPrev, ref float tiempoPrev)
        {
            try
            {
                if (!File.Exists("/proc/self/stat")) return null;
                string s = File.ReadAllText("/proc/self/stat");
                var p = s.Substring(s.LastIndexOf(')') + 2).Split(' ');
                double ticks = double.Parse(p[11]) + double.Parse(p[12]);   // utime + stime, en 1/100 s
                float ahora = Time.realtimeSinceStartup;
                float? r = null;
                if (tiempoPrev >= 0 && ahora > tiempoPrev)
                {
                    double seg = (ticks - ticksPrev) / 100.0;
                    r = Mathf.Clamp01((float)(seg / ((ahora - tiempoPrev) * Mathf.Max(1, SystemInfo.processorCount))));
                }
                ticksPrev = ticks; tiempoPrev = ahora;
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
        // Construccion de tarjetas
        // ------------------------------------------------------------------
        void Construir()
        {
            if (!contenedorLista) return;

            Seccion("Ahora mismo");
            fps = Tarjeta("Rendimiento", "Fotogramas por segundo");
            cpu = Tarjeta("Procesador", "Uso de CPU de la app");
            ram = Tarjeta("Memoria de la app", "RAM que ocupa Frai");
            sistema = Tarjeta("Memoria del celular", "RAM en uso por todo el sistema");
            graficos = Tarjeta("Gráficos", "Memoria de video");
            bateria = Tarjeta("Batería", "Nivel y temperatura");

            Seccion("Estadísticas");
            var hist = CrearTarjeta("Historial · últimos 5 min");
            gFps = CrearGrafica(hist, "FPS", Verde);
            gCpu = CrearGrafica(hist, "CPU de la app", Azul);
            gRam = CrearGrafica(hist, "Memoria de la app", Ambar);

            var ses = CrearTarjeta("Esta sesión");
            txTiempo = Linea(ses, "Tiempo de uso");
            txFps = Linea(ses, "FPS");
            txCpu = Linea(ses, "CPU");
            txRam = Linea(ses, "Memoria");
            txTemp = Linea(ses, "Temp. máxima batería");
            txBateria = Linea(ses, "Batería gastada");

            var acu = CrearTarjeta("Acumulado · todas las sesiones");
            txSesiones = Linea(acu, "Sesiones");
            txTiempoTotal = Linea(acu, "Tiempo total de uso");
            txConsumoMedio = Linea(acu, "Consumo medio de batería");
            txAutonomia = Linea(acu, "Autonomía estimada");

            var nota = Texto(contenedorLista, "Nota", 22, Suave, FontStyles.Normal, TextAlignmentOptions.Center);
            nota.margin = new Vector4(24, 6, 24, 0);
            nota.text = $"{SystemInfo.deviceModel} · {SystemInfo.operatingSystem}\n" +
                        $"Modo de la guía: {DeviceTier.Detectar(true)}\n" +
                        "El consumo de batería incluye pantalla, cámara y GPS mientras usas la app; solo se cuenta cuando el celular no está cargando. " +
                        "Es una estimación: mejora cuantos más minutos de uso acumule.";
        }

        void Seccion(string titulo)
        {
            var t = Texto(contenedorLista, titulo, 34, Color.white, FontStyles.Bold, TextAlignmentOptions.BottomLeft);
            t.margin = new Vector4(8, 6, 0, 0);
        }

        GameObject CrearTarjeta(string titulo)
        {
            var go = new GameObject("Tarjeta_" + titulo, typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            go.transform.SetParent(contenedorLista, false);
            var img = go.GetComponent<Image>();
            img.sprite = spriteTarjeta; img.type = Image.Type.Sliced; img.color = Fondo; img.raycastTarget = false;
            var v = go.GetComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(32, 32, 24, 26); v.spacing = 12;
            v.childControlWidth = true; v.childControlHeight = true;
            v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            if (!string.IsNullOrEmpty(titulo)) Texto(go.transform, titulo, 30, Color.white, FontStyles.Bold, TextAlignmentOptions.Left);
            return go;
        }

        // Linea "etiqueta ........ valor" dentro de una tarjeta
        TMP_Text Linea(GameObject tarjeta, string etiqueta)
        {
            var fila = new GameObject("Linea", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            fila.transform.SetParent(tarjeta.transform, false);
            var h = fila.GetComponent<HorizontalLayoutGroup>();
            h.spacing = 16; h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = true; h.childControlHeight = true;
            h.childForceExpandWidth = false; h.childForceExpandHeight = false;
            var e = Texto(fila.transform, etiqueta, 26, Suave, FontStyles.Normal, TextAlignmentOptions.Left);
            e.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            var v = Texto(fila.transform, "--", 26, Color.white, FontStyles.Bold, TextAlignmentOptions.Right);
            v.textWrappingMode = TextWrappingModes.NoWrap;
            return v;
        }

        Grafica CrearGrafica(GameObject tarjeta, string nombre, Color color)
        {
            var g = new Grafica { color = color, barras = new Image[Muestras] };

            var cab = new GameObject("Cabecera", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            cab.transform.SetParent(tarjeta.transform, false);
            var h = cab.GetComponent<HorizontalLayoutGroup>();
            h.childControlWidth = true; h.childControlHeight = true;
            h.childForceExpandWidth = false; h.childForceExpandHeight = false;
            var n = Texto(cab.transform, nombre, 24, Suave, FontStyles.Normal, TextAlignmentOptions.Left);
            n.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            g.valor = Texto(cab.transform, "--", 24, Color.white, FontStyles.Bold, TextAlignmentOptions.Right);
            g.valor.textWrappingMode = TextWrappingModes.NoWrap;

            var area = new GameObject("Grafica", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            area.transform.SetParent(tarjeta.transform, false);
            var ai = area.GetComponent<Image>();
            ai.color = new Color(1, 1, 1, 0.06f); ai.raycastTarget = false;
            var le = area.GetComponent<LayoutElement>(); le.minHeight = le.preferredHeight = 96;

            for (int i = 0; i < Muestras; i++)
            {
                var b = new GameObject("b" + i, typeof(RectTransform), typeof(Image));
                b.transform.SetParent(area.transform, false);
                var bi = b.GetComponent<Image>();
                bi.color = new Color(color.r, color.g, color.b, 0.9f); bi.raycastTarget = false;
                var rt = (RectTransform)b.transform;
                rt.anchorMin = new Vector2(i / (float)Muestras + 0.002f, 0);
                rt.anchorMax = new Vector2((i + 1) / (float)Muestras - 0.002f, 0.02f);
                rt.offsetMin = rt.offsetMax = Vector2.zero;
                g.barras[i] = bi;
            }
            return g;
        }

        Fila Tarjeta(string titulo, string subtitulo)
        {
            var go = CrearTarjeta(null);
            go.name = "Recurso_" + titulo;
            go.GetComponent<VerticalLayoutGroup>().spacing = 10;

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
