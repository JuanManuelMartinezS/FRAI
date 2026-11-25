using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GuiaFrailejon
{
    /// <summary>
    /// Mapita esquematico del recorrido: una linea con las 4 estaciones.
    /// - Resalta la estacion actual y las ya visitadas.
    /// - Si hay GPS y coordenadas de las estaciones, muestra un punto con la posicion del visitante en la ruta.
    /// - Tocar una estacion la selecciona (util en el modo sin AR).
    /// Se construye solo al iniciar; va en un RectTransform vacio.
    /// </summary>
    public class MiniMapa : MonoBehaviour
    {
        public KnowledgeBase kb;
        public GeoLocalizador geo;
        public Sprite circulo;

        public Color colorLinea = new Color(1f, 1f, 1f, 0.30f);
        public Color colorVisitada = new Color(0.40f, 0.85f, 0.65f);
        public Color colorActual = new Color(1f, 0.80f, 0.15f);
        public Color colorPendiente = new Color(0.80f, 0.90f, 1f, 0.90f);
        public Color colorUsuario = new Color(0.30f, 0.70f, 1f);

        public event Action<int> AlTocarEstacion;
        /// <summary>Texto corto para mostrar, p. ej. "Valle Lunar a 350 m". Vacio si no hay GPS.</summary>
        public string TextoDistancia { get; private set; } = "";

        const float X0 = 0.08f, X1 = 0.92f, YLinea = 0.64f;
        readonly List<Image> nodos = new List<Image>();
        readonly List<TMP_Text> etiquetas = new List<TMP_Text>();
        readonly HashSet<int> visitadas = new HashSet<int>();
        RectTransform progreso;
        RectTransform puntoUsuario;
        int actual = -1;
        float proximaRevision;

        void Start()
        {
            if (kb == null) return;
            kb.AlCargar += Construir;
            if (kb.Listo) Construir();
        }

        void Construir()
        {
            for (int i = transform.childCount - 1; i >= 0; i--) Destroy(transform.GetChild(i).gameObject);
            nodos.Clear(); etiquetas.Clear();
            var est = kb.Datos.estaciones;
            int n = est.Length;
            if (n == 0) return;

            Linea("Linea", X0, X1, colorLinea);
            progreso = Linea("Progreso", X0, X0, colorVisitada);

            for (int i = 0; i < n; i++)
            {
                float x = PosX(i, n);
                var nodo = Imagen("Estacion" + (i + 1), circulo, colorPendiente);
                Anclar(nodo.rectTransform, x, YLinea, 54, 54);
                var btn = nodo.gameObject.AddComponent<Button>();
                int idx = i;
                btn.onClick.AddListener(() => AlTocarEstacion?.Invoke(idx));
                nodos.Add(nodo);

                var num = Texto(nodo.transform, (i + 1).ToString(), 26, new Color(0.04f, 0.12f, 0.27f));
                num.fontStyle = FontStyles.Bold;
                num.rectTransform.anchorMin = Vector2.zero; num.rectTransform.anchorMax = Vector2.one;
                num.rectTransform.sizeDelta = Vector2.zero;

                string nombre = string.IsNullOrEmpty(est[i].corto) ? est[i].nombre : est[i].corto;
                var et = Texto(transform, nombre, 25, Color.white);
                Anclar(et.rectTransform, x, 0.17f, 240, 38);
                etiquetas.Add(et);
            }

            var pu = Imagen("TuPosicion", circulo, colorUsuario);
            puntoUsuario = pu.rectTransform;
            Anclar(puntoUsuario, X0, YLinea, 22, 22);
            puntoUsuario.anchoredPosition = new Vector2(0, 30);
            pu.raycastTarget = false;
            puntoUsuario.gameObject.SetActive(false);

            Refrescar();
        }

        public void MarcarActual(string estacionId)
        {
            if (kb == null || kb.Datos == null) return;
            int idx = Array.FindIndex(kb.Datos.estaciones, e => e.id == estacionId);
            if (idx < 0) return;
            actual = idx;
            visitadas.Add(idx);
            Refrescar();
        }

        void Refrescar()
        {
            int n = nodos.Count;
            for (int i = 0; i < n; i++)
            {
                bool esActual = i == actual;
                nodos[i].color = esActual ? colorActual : visitadas.Contains(i) ? colorVisitada : colorPendiente;
                nodos[i].rectTransform.localScale = Vector3.one * (esActual ? 1.2f : 1f);
                etiquetas[i].fontStyle = esActual ? FontStyles.Bold : FontStyles.Normal;
                etiquetas[i].color = esActual ? colorActual : Color.white;
            }
            if (progreso != null && n > 0)
            {
                float x = actual >= 0 ? PosX(actual, n) : X0;
                progreso.anchorMax = new Vector2(x, YLinea);
            }
        }

        void Update()
        {
            if (Time.time < proximaRevision || puntoUsuario == null) return;
            proximaRevision = Time.time + 1f;

            if (geo == null || !geo.Activo || !geo.HayCoordenadas)
            {
                puntoUsuario.gameObject.SetActive(false);
                TextoDistancia = "";
                return;
            }

            float t = PosicionEnRuta(out int siguiente, out float distSiguiente);
            int n = nodos.Count;
            float x = Mathf.Lerp(X0, X1, n > 1 ? t / (n - 1) : 0.5f);
            puntoUsuario.anchorMin = puntoUsuario.anchorMax = new Vector2(x, YLinea);
            puntoUsuario.gameObject.SetActive(true);

            var est = kb.Datos.estaciones;
            TextoDistancia = siguiente >= 0 && distSiguiente > 40
                ? $"{(string.IsNullOrEmpty(est[siguiente].corto) ? est[siguiente].nombre : est[siguiente].corto)} a {Redondear(distSiguiente)}"
                : "";
        }

        /// <summary>Proyecta la posicion GPS sobre la ruta. Devuelve 0..(n-1) (indice fraccionario de estacion).</summary>
        float PosicionEnRuta(out int siguiente, out float distSiguiente)
        {
            var est = kb.Datos.estaciones;
            siguiente = -1; distSiguiente = 0;
            double lat0 = geo.Lat, lon0 = geo.Lon;
            double kx = Math.Cos(lat0 * Math.PI / 180) * 111320, ky = 110540;
            Vector2 P(Estacion e) => new Vector2((float)((e.lon - lon0) * kx), (float)((e.lat - lat0) * ky));   // usuario en (0,0)

            float mejorD = float.MaxValue, mejorT = 0;
            for (int i = 0; i < est.Length - 1; i++)
            {
                if (!GeoLocalizador.Valida(est[i]) || !GeoLocalizador.Valida(est[i + 1])) continue;
                Vector2 a = P(est[i]), b = P(est[i + 1]), ab = b - a;
                float u = ab.sqrMagnitude > 0.01f ? Mathf.Clamp01(Vector2.Dot(-a, ab) / ab.sqrMagnitude) : 0;
                float d = (a + ab * u).magnitude;
                if (d < mejorD) { mejorD = d; mejorT = i + u; }
            }

            int sig = Mathf.Clamp(Mathf.CeilToInt(mejorT + 0.01f), 0, est.Length - 1);
            if (GeoLocalizador.Valida(est[sig]))
            {
                siguiente = sig;
                distSiguiente = GeoLocalizador.Distancia(lat0, lon0, est[sig].lat, est[sig].lon);
            }
            return mejorT;
        }

        static string Redondear(float m) => m >= 1000 ? $"{m / 1000f:0.0} km" : $"{Mathf.Round(m / 10f) * 10:0} m";

        static float PosX(int i, int n) => n <= 1 ? 0.5f : Mathf.Lerp(X0, X1, i / (float)(n - 1));

        RectTransform Linea(string nombre, float xa, float xb, Color color)
        {
            var img = Imagen(nombre, null, color);
            img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = new Vector2(xa, YLinea);
            rt.anchorMax = new Vector2(xb, YLinea);
            rt.pivot = new Vector2(0, 0.5f);
            rt.sizeDelta = new Vector2(0, 6);
            rt.anchoredPosition = Vector2.zero;
            return rt;
        }

        Image Imagen(string nombre, Sprite sprite, Color color)
        {
            var go = new GameObject(nombre, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            return img;
        }

        static TMP_Text Texto(Transform padre, string texto, float tam, Color color)
        {
            var go = new GameObject("Etiqueta", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(padre, false);
            var t = go.GetComponent<TextMeshProUGUI>();
            t.text = texto;
            t.fontSize = tam;
            t.color = color;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Truncate;
            t.raycastTarget = false;
            return t;
        }

        static void Anclar(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(x, y);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = Vector2.zero;
        }
    }
}
