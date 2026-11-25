using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GuiaFrailejon
{
    /// <summary>
    /// Pestana "Clima y alertas": lectura actual de la estacion (LoRa), estado del enlace, alertas y recomendaciones.
    /// Va en un objeto siempre activo para poder avisar (puntito rojo en la pestana y nota en el chat)
    /// aunque el visitante este en otra pestana.
    /// </summary>
    public class ClimaUI : MonoBehaviour
    {
        [Header("Logica")]
        public FuenteClima fuente;
        public NavegacionUI navegacion;
        public GuideUI guia;

        [Header("Lectura actual")]
        public Image iconoCondicion;
        public TMP_Text textoTemperatura;
        public TMP_Text textoCondicion;
        public TMP_Text textoHumedad;
        public TMP_Text textoViento;
        public TMP_Text textoPresion;

        [Header("Estado del enlace")]
        public Image iconoEstado;
        public Image iconoEstadoBarra;
        public TMP_Text textoEstado;
        public TMP_Text textoEstadoDetalle;

        [Header("Listas")]
        public RectTransform contenedorAlertas;
        public RectTransform contenedorRecomendaciones;
        public Button botonEmergencia;
        public Button botonVolver;

        [Header("Recursos")]
        public Sprite spriteTarjeta;
        public Sprite spritePildora;
        public Sprite[] iconos;

        [Tooltip("Recomendaciones que siempre se muestran en el paramo.")]
        public string[] recomendacionesBase =
        {
            "Lleva ropa impermeable y abrigo por capas",
            "Camina despacio e hidrátate: estás a gran altura",
            "No te salgas del sendero señalizado",
        };

        static readonly Color ColPeligro = new Color(0.86f, 0.18f, 0.20f, 0.95f);
        static readonly Color ColPrecaucion = new Color(1f, 0.76f, 0.14f, 0.95f);
        static readonly Color ColInfo = new Color(0.16f, 0.52f, 1f, 0.85f);
        static readonly Color ColOk = new Color(0.13f, 0.62f, 0.36f, 0.92f);
        static readonly Color Verde = new Color(0.30f, 0.90f, 0.50f);
        static readonly Color Gris = new Color(0.76f, 0.86f, 1f, 0.55f);
        static readonly Color Ambar = new Color(1f, 0.79f, 0.16f);

        readonly HashSet<string> avisadas = new HashSet<string>();
        readonly Dictionary<string, Sprite> porNombre = new Dictionary<string, Sprite>();
        float proximoReloj;

        void Start()
        {
            if (iconos != null)
                foreach (var s in iconos) if (s) porNombre[s.name.Replace("Icono_", "")] = s;
            if (fuente) fuente.AlActualizar += Refrescar;
            if (navegacion) navegacion.AlCambiar += i => { if (i == NavegacionUI.Clima) { navegacion.Insignia(NavegacionUI.Clima, false); Refrescar(); } };
            if (botonVolver) botonVolver.onClick.AddListener(() => navegacion.Ir(NavegacionUI.Guia));
            if (botonEmergencia) botonEmergencia.onClick.AddListener(() =>
            {
                navegacion.Ir(NavegacionUI.Guia);
                if (guia) guia.Preguntar("emergencia");
            });
            Refrescar();
        }

        void Update()
        {
            // Refresca el "hace X min" sin esperar una lectura nueva
            if (Time.unscaledTime < proximoReloj) return;
            proximoReloj = Time.unscaledTime + 20f;
            PintarEstado();
        }

        Sprite Icono(string n) => n != null && porNombre.TryGetValue(n, out var s) ? s : null;

        void Refrescar()
        {
            var l = fuente ? fuente.Ultima : null;

            // ---- Lectura ----
            string cond = FuenteClima.Condicion(l, out string ic);
            if (iconoCondicion) { iconoCondicion.sprite = Icono(ic); iconoCondicion.color = ic == "sol" || ic == "solnube" ? Ambar : Color.white; }
            if (textoCondicion) textoCondicion.text = cond;
            if (textoTemperatura) textoTemperatura.text = l != null && LecturaClima.Hay(l.temperatura) ? $"{l.temperatura:0}°<size=45%>C</size>" : "--°";
            if (textoHumedad) textoHumedad.text = Valor(l?.humedad, "0", " %");
            if (textoViento) textoViento.text = Valor(l?.viento, "0", " km/h");
            if (textoPresion) textoPresion.text = Valor(l?.presion, "0", " hPa");
            PintarEstado();

            // ---- Alertas ----
            if (contenedorAlertas)
            {
                Vaciar(contenedorAlertas);
                var lista = fuente ? fuente.Alertas : new List<AlertaClima>();
                if (l == null)
                    TarjetaAlerta("info", "Esperando datos de la estación", "Cuando el sensor meteorológico envíe datos, aquí verás las alertas.", ColInfo, Color.white);
                else if (lista.Count == 0)
                    TarjetaAlerta("check", "Sin alertas activas", "Las condiciones actuales son normales para el páramo. Igual, ve preparado.", ColOk, Color.white);
                foreach (var a in lista)
                {
                    bool amb = a.nivel == NivelAlerta.Precaucion;
                    TarjetaAlerta(a.icono, a.titulo, a.mensaje,
                        a.nivel == NivelAlerta.Peligro ? ColPeligro : amb ? ColPrecaucion : ColInfo,
                        amb ? new Color(0.10f, 0.12f, 0.16f) : Color.white);
                }
            }

            // ---- Recomendaciones ----
            if (contenedorRecomendaciones)
            {
                Vaciar(contenedorRecomendaciones, 1);   // el primer hijo es el titulo
                var recs = new List<string>();
                if (fuente) foreach (var a in fuente.Alertas) foreach (var r in a.recomendaciones) if (!recs.Contains(r)) recs.Add(r);
                foreach (var r in recomendacionesBase) if (recs.Count < 5 && !recs.Contains(r)) recs.Add(r);
                foreach (var r in recs) FilaRecomendacion(r);
            }

            // ---- Avisos fuera de la pestana ----
            if (fuente && navegacion)
            {
                bool hay = fuente.NivelMaximo.HasValue && fuente.NivelMaximo.Value >= NivelAlerta.Precaucion;
                navegacion.Insignia(NavegacionUI.Clima, hay);
                foreach (var a in fuente.Alertas)
                    if (a.nivel == NivelAlerta.Peligro && avisadas.Add(a.id) && guia)
                        guia.Nota($"<b>Alerta: {a.titulo}.</b> {a.mensaje} Mira la pestaña Clima.", true);
            }
        }

        void PintarEstado()
        {
            if (fuente == null) return;
            string titulo, detalle;
            Color c;
            switch (fuente.Estado)
            {
                case EstadoClima.Conectada:
                    titulo = "Datos recibidos por LoRa"; c = Verde;
                    detalle = $"Última actualización: {Hace(fuente.UltimaRecepcion)}";
                    if (fuente.Ultima != null && LecturaClima.Hay(fuente.Ultima.rssi)) detalle += $" · señal {fuente.Ultima.rssi:0} dBm";
                    break;
                case EstadoClima.Desactualizada:
                    titulo = "Sin datos recientes"; c = Ambar;
                    detalle = $"Última lectura: {Hace(fuente.UltimaRecepcion)}";
                    break;
                case EstadoClima.Simulada:
                    titulo = "Datos simulados (demostración)"; c = Ambar;
                    detalle = "El sensor LoRa aún no está conectado";
                    break;
                default:
                    titulo = "Estación meteorológica sin conexión"; c = Gris;
                    detalle = "Buscando datos del sensor…";
                    break;
            }
            if (textoEstado) textoEstado.text = titulo;
            if (textoEstadoDetalle) textoEstadoDetalle.text = detalle;
            if (iconoEstado) iconoEstado.color = c;
            if (iconoEstadoBarra) iconoEstadoBarra.color = c;
        }

        static string Hace(DateTime t)
        {
            double m = (DateTime.Now - t).TotalMinutes;
            return m < 1 ? $"ahora ({t:HH:mm})" : m < 60 ? $"hace {m:0} min ({t:HH:mm})" : t.ToString("HH:mm");
        }

        static string Valor(float? v, string fmt, string unidad) =>
            v.HasValue && LecturaClima.Hay(v.Value) ? v.Value.ToString(fmt) + unidad : "--";

        static void Vaciar(Transform t, int desde = 0)
        {
            for (int i = t.childCount - 1; i >= desde; i--) Destroy(t.GetChild(i).gameObject);
        }

        // ------------------------------------------------------------------
        // Construccion de filas por codigo
        // ------------------------------------------------------------------
        void TarjetaAlerta(string icono, string titulo, string mensaje, Color fondo, Color texto)
        {
            var go = new GameObject("Alerta", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup));
            go.transform.SetParent(contenedorAlertas, false);
            var img = go.GetComponent<Image>();
            img.sprite = spriteTarjeta; img.type = Image.Type.Sliced; img.color = fondo; img.raycastTarget = false;
            var h = go.GetComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(28, 28, 26, 26); h.spacing = 24;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = true; h.childControlHeight = true;
            h.childForceExpandWidth = false; h.childForceExpandHeight = false;

            var circ = new GameObject("Icono", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            circ.transform.SetParent(go.transform, false);
            var ci = circ.GetComponent<Image>();
            ci.sprite = spritePildora; ci.color = new Color(1, 1, 1, 0.22f); ci.raycastTarget = false;
            var le = circ.GetComponent<LayoutElement>();
            le.minWidth = le.preferredWidth = 88; le.minHeight = le.preferredHeight = 88;
            var ico = new GameObject("Glifo", typeof(RectTransform), typeof(Image));
            ico.transform.SetParent(circ.transform, false);
            var ii = ico.GetComponent<Image>();
            ii.sprite = Icono(icono) ?? Icono("alerta"); ii.color = texto; ii.raycastTarget = false;
            var irt = (RectTransform)ico.transform; irt.sizeDelta = new Vector2(52, 52);

            var col = new GameObject("Textos", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            col.transform.SetParent(go.transform, false);
            col.GetComponent<LayoutElement>().flexibleWidth = 1;
            var v = col.GetComponent<VerticalLayoutGroup>();
            v.spacing = 4; v.childControlWidth = true; v.childControlHeight = true;
            v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            Texto(col.transform, titulo, 32, texto, FontStyles.Bold);
            Texto(col.transform, mensaje, 26, new Color(texto.r, texto.g, texto.b, 0.9f), FontStyles.Normal);
        }

        void FilaRecomendacion(string texto)
        {
            var go = new GameObject("Recomendacion", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            go.transform.SetParent(contenedorRecomendaciones, false);
            var h = go.GetComponent<HorizontalLayoutGroup>();
            h.spacing = 18; h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = true; h.childControlHeight = true;
            h.childForceExpandWidth = false; h.childForceExpandHeight = false;

            var circ = new GameObject("Check", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            circ.transform.SetParent(go.transform, false);
            var ci = circ.GetComponent<Image>();
            ci.sprite = spritePildora; ci.color = new Color(0.22f, 0.78f, 0.42f); ci.raycastTarget = false;
            var le = circ.GetComponent<LayoutElement>();
            le.minWidth = le.preferredWidth = 44; le.minHeight = le.preferredHeight = 44;
            var ico = new GameObject("Glifo", typeof(RectTransform), typeof(Image));
            ico.transform.SetParent(circ.transform, false);
            var ii = ico.GetComponent<Image>();
            ii.sprite = Icono("check"); ii.raycastTarget = false;
            ((RectTransform)ico.transform).sizeDelta = new Vector2(32, 32);

            var t = Texto(go.transform, texto, 28, Color.white, FontStyles.Normal);
            t.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
        }

        static TMP_Text Texto(Transform padre, string texto, float tam, Color color, FontStyles estilo)
        {
            var go = new GameObject("Texto", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(padre, false);
            var t = go.GetComponent<TextMeshProUGUI>();
            t.text = texto; t.fontSize = tam; t.color = color; t.fontStyle = estilo;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.raycastTarget = false;
            return t;
        }
    }
}
