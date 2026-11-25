using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace GuiaFrailejon
{
    /// <summary>
    /// Lectura de la estacion meteorologica (ESP32 + sensores, enviada por LoRa a la estacion del sendero).
    /// Formato JSON esperado (los campos que falten se dejan en -999):
    /// { "estacion":"estacion_1", "marca":1764000000, "temperatura":6.5, "humedad":92, "presion":622.4,
    ///   "viento":18, "lluvia":0.4, "rssi":-97, "bateria":81 }
    /// </summary>
    [Serializable]
    public class LecturaClima
    {
        public const float SinDato = -999;
        public string estacion = "";
        public long marca;                     // unix (s) en que se midio
        public float temperatura = SinDato;    // °C
        public float humedad = SinDato;        // %
        public float presion = SinDato;        // hPa
        public float viento = SinDato;         // km/h
        public float lluvia = SinDato;         // mm/h
        public float rssi = SinDato;           // dBm del enlace LoRa
        public float bateria = SinDato;        // % del nodo
        public static bool Hay(float v) => v > SinDato + 1;
    }

    public enum NivelAlerta { Info, Precaucion, Peligro }

    public class AlertaClima
    {
        public string id, titulo, mensaje, icono;
        public NivelAlerta nivel;
        public string[] recomendaciones = new string[0];
    }

    public enum EstadoClima { SinDatos, Conectada, Desactualizada, Simulada }

    /// <summary>
    /// Obtiene las lecturas del clima y calcula las alertas.
    /// Modos:
    ///  - Simulada: datos de demostracion (mientras el LoRa/ESP32 no esta listo). La UI lo dice claramente.
    ///  - Url: consulta periodicamente un JSON (p. ej. el servidor de la estacion: http://192.168.4.1/api/clima).
    ///  - Manual: otro componente (BLE, serial, etc.) llama a Recibir(json).
    /// Los umbrales son genericos para paramo: VALIDARLOS con el PNN antes de produccion.
    /// </summary>
    public class FuenteClima : MonoBehaviour
    {
        public enum Modo { Simulada, Url, Manual }

        public Modo modo = Modo.Simulada;
        public string url = "http://192.168.4.1/api/clima";
        public float intervaloSegundos = 30;
        [Tooltip("Minutos sin datos nuevos para considerar la lectura desactualizada.")]
        public float minutosVigencia = 15;

        [Header("Umbrales (validar con el PNN)")]
        public float tempFrio = 4f;
        public float tempHelada = 0f;
        public float vientoFuerte = 30f;
        public float vientoPeligro = 50f;
        public float lluviaModerada = 1f;
        public float lluviaFuerte = 5f;
        public float humedadNiebla = 95f;
        public float caidaPresion3h = 2f;

        public LecturaClima Ultima { get; private set; }
        public DateTime UltimaRecepcion { get; private set; }
        public EstadoClima Estado { get; private set; } = EstadoClima.SinDatos;
        public List<AlertaClima> Alertas { get; } = new List<AlertaClima>();
        public event Action AlActualizar;

        readonly List<(float t, float presion)> historial = new List<(float, float)>();

        IEnumerator Start()
        {
            while (true)
            {
                switch (modo)
                {
                    case Modo.Simulada: Procesar(Simular()); break;
                    case Modo.Url: yield return Consultar(); break;
                }
                RevisarVigencia();
                yield return new WaitForSecondsRealtime(modo == Modo.Simulada ? 5f : Mathf.Max(5, intervaloSegundos));
            }
        }

        /// <summary>Para puentes BLE/serial/LoRa: entregar el JSON de una lectura.</summary>
        public void Recibir(string json)
        {
            try { Procesar(JsonUtility.FromJson<LecturaClima>(json)); }
            catch (Exception e) { Debug.LogWarning("[Clima] JSON invalido: " + e.Message); }
        }

        IEnumerator Consultar()
        {
            using (var req = UnityWebRequest.Get(url))
            {
                req.timeout = 6;
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success) Recibir(req.downloadHandler.text);
            }
        }

        void RevisarVigencia()
        {
            if (Ultima == null || modo == Modo.Simulada) return;
            bool vieja = (DateTime.Now - UltimaRecepcion).TotalMinutes > minutosVigencia;
            var nuevo = vieja ? EstadoClima.Desactualizada : EstadoClima.Conectada;
            if (nuevo != Estado) { Estado = nuevo; AlActualizar?.Invoke(); }
        }

        void Procesar(LecturaClima l)
        {
            if (l == null) return;
            Ultima = l;
            UltimaRecepcion = DateTime.Now;
            Estado = modo == Modo.Simulada ? EstadoClima.Simulada : EstadoClima.Conectada;

            if (LecturaClima.Hay(l.presion))
            {
                historial.Add((Time.realtimeSinceStartup, l.presion));
                historial.RemoveAll(h => Time.realtimeSinceStartup - h.t > 3 * 3600);
            }
            CalcularAlertas(l);
            AlActualizar?.Invoke();
        }

        // ------------------------------------------------------------------
        // Reglas de alerta
        // ------------------------------------------------------------------
        void CalcularAlertas(LecturaClima l)
        {
            Alertas.Clear();
            float t = l.temperatura, v = l.viento, ll = l.lluvia, h = l.humedad;

            if (LecturaClima.Hay(ll) && ll >= lluviaFuerte)
                Agregar("lluvia_fuerte", NivelAlerta.Peligro, "lluvia", "Lluvia fuerte",
                    "Está lloviendo fuerte en la estación. Los senderos pueden estar resbalosos.",
                    "Usa ropa impermeable", "Camina despacio y por el sendero", "Busca refugio si hay rayos");
            else if (LecturaClima.Hay(ll) && ll >= lluviaModerada)
                Agregar("lluvia", NivelAlerta.Precaucion, "lluvia", "Lluvia",
                    "Hay lluvia en la zona. Protege tu equipo y abrígate.",
                    "Usa ropa impermeable", "Protege el celular del agua");

            if (LecturaClima.Hay(t) && t <= tempHelada)
                Agregar("helada", NivelAlerta.Peligro, "termometro", "Temperatura bajo cero",
                    $"Hace {t:0} °C: riesgo de hipotermia y de hielo en el camino.",
                    "Ponte todas las capas de abrigo", "Cubre manos, cabeza y cuello", "Cuidado con el hielo");
            else if (LecturaClima.Hay(t) && t <= tempFrio)
                Agregar("frio", NivelAlerta.Precaucion, "termometro", "Frío intenso",
                    $"Hace {t:0} °C. El frío del páramo cansa más de lo que parece.",
                    "Abrígate por capas", "Mantente seco");

            if (LecturaClima.Hay(v) && v >= vientoPeligro)
                Agregar("viento_fuerte", NivelAlerta.Peligro, "viento", "Viento muy fuerte",
                    $"Ráfagas de {v:0} km/h. Evita zonas expuestas y bordes.",
                    "Aléjate de bordes y zonas altas", "Mantén el grupo unido");
            else if (LecturaClima.Hay(v) && v >= vientoFuerte)
                Agregar("viento", NivelAlerta.Precaucion, "viento", "Viento fuerte",
                    $"Viento de {v:0} km/h. La sensación térmica es más baja.",
                    "Usa cortavientos", "Mantén el grupo unido");

            if (LecturaClima.Hay(h) && h >= humedadNiebla)
                Agregar("niebla", NivelAlerta.Precaucion, "niebla", "Niebla: poca visibilidad",
                    "La humedad es muy alta y puede formarse niebla de repente.",
                    "No te salgas del sendero", "Mantén el grupo a la vista");

            if (historial.Count >= 2 && historial[0].presion - historial[historial.Count - 1].presion >= caidaPresion3h)
                Agregar("presion", NivelAlerta.Info, "presion", "Posible cambio de tiempo",
                    "La presión está bajando: puede llegar mal tiempo en las próximas horas.",
                    "Planea el regreso con tiempo");

            Alertas.Sort((a, b) => b.nivel.CompareTo(a.nivel));
        }

        void Agregar(string id, NivelAlerta n, string icono, string titulo, string msg, params string[] recs) =>
            Alertas.Add(new AlertaClima { id = id, nivel = n, icono = icono, titulo = titulo, mensaje = msg, recomendaciones = recs });

        public NivelAlerta? NivelMaximo => Alertas.Count > 0 ? Alertas[0].nivel : (NivelAlerta?)null;

        /// <summary>"Nublado", "Lluvia", ... a partir de los sensores (no es un pronostico).</summary>
        public static string Condicion(LecturaClima l, out string icono)
        {
            if (l == null) { icono = "nube"; return "Sin datos"; }
            if (LecturaClima.Hay(l.lluvia) && l.lluvia >= 0.2f) { icono = "lluvia"; return "Lluvia"; }
            if (LecturaClima.Hay(l.humedad) && l.humedad >= 95) { icono = "niebla"; return "Niebla"; }
            if (LecturaClima.Hay(l.humedad) && l.humedad >= 82) { icono = "nube"; return "Nublado"; }
            if (LecturaClima.Hay(l.humedad) && l.humedad >= 65) { icono = "solnube"; return "Parcialmente nublado"; }
            icono = "sol"; return "Despejado";
        }

        // ------------------------------------------------------------------
        // Datos simulados (para probar la interfaz sin el hardware)
        // ------------------------------------------------------------------
        LecturaClima Simular()
        {
            float s = Time.realtimeSinceStartup / 60f;   // cambia lento (minutos)
            float n(float off) => Mathf.PerlinNoise(s * 0.6f + off, off * 1.7f);
            return new LecturaClima
            {
                estacion = "simulada",
                marca = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                temperatura = Mathf.Round((2f + n(1) * 10f) * 10) / 10,     // 2..12 °C
                humedad = Mathf.Round(70 + n(2) * 30),                     // 70..100 %
                presion = Mathf.Round((618 + n(3) * 8) * 10) / 10,          // ~4000 m s.n.m.
                viento = Mathf.Round(5 + n(4) * 40),                        // 5..45 km/h
                lluvia = Mathf.Max(0, Mathf.Round((n(5) - 0.55f) * 20 * 10) / 10),
                rssi = Mathf.Round(-80 - n(6) * 30),
                bateria = 80,
            };
        }
    }
}
