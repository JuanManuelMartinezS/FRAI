using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GuiaFrailejon
{
    /// <summary>
    /// Pestana "Explorar": escaneo continuo con la camara e identificacion de especies.
    /// - Marco de escaneo con linea animada; se pone verde cuando identifica.
    /// - Tarjeta inferior con miniatura (lo que ve el modelo), nombre, confianza y descripcion.
    /// - "Preguntar a Frai" lleva la especie a la conversacion.
    /// Va en un objeto siempre activo; el panel visual se muestra/oculta con la navegacion.
    /// </summary>
    public class ExplorarUI : MonoBehaviour
    {
        [Header("Logica")]
        public ClasificadorEspecies clasificador;
        public CapturaCamara captura;
        public NavegacionUI navegacion;
        public GuideUI guia;
        [Tooltip("Segundos entre inferencias.")]
        public float intervalo = 0.5f;
        [Tooltip("Suavizado temporal: peso del cuadro nuevo (como TemporalSmoother de la PWA).")]
        [Range(0.05f, 1f)] public float alfa = 0.45f;

        [Header("Escaneo")]
        public Image[] esquinas;
        public RectTransform lineaEscaneo;
        public float altoMarco = 640;
        public TMP_Text textoAyuda;
        public GameObject aviso;
        public TMP_Text textoAviso;

        [Header("Etiqueta sobre el marco")]
        public GameObject etiqueta;
        public TMP_Text textoEtiqueta;

        [Header("Tarjeta de resultado")]
        public RawImage miniatura;
        public TMP_Text textoTipo;
        public TMP_Text textoNombre;
        public TMP_Text textoCientifico;
        public GameObject barra;
        public RectTransform rellenoBarra;
        public Image imagenRelleno;
        public TMP_Text textoConfianza;
        public TMP_Text textoDescripcion;
        public Button botonPreguntar;
        public Button botonVolver;

        public Color colorNormal = Color.white;
        public Color colorIdentificado = new Color(0.30f, 0.90f, 0.50f);
        public Color colorDuda = new Color(1f, 0.79f, 0.16f);

        float[] acumulado;
        Texture2D texMini;
        Color32[] pxMini;
        DecisionVision ultima;
        string ultimaHablada;
        Coroutine bucle;
        float fase;

        void Start()
        {
            if (navegacion) navegacion.AlCambiar += i => { if (i == NavegacionUI.Explorar) Abrir(); else Cerrar(); };
            if (botonPreguntar) botonPreguntar.onClick.AddListener(Preguntar);
            if (botonVolver) botonVolver.onClick.AddListener(() => navegacion.Ir(NavegacionUI.Guia));
        }

        void Abrir()
        {
            acumulado = null;
            ultima = null;
            MostrarBuscando();
            if (clasificador == null || !clasificador.Disponible)
            {
                MostrarAviso(clasificador ? clasificador.Motivo : "Falta el clasificador de especies.");
                MostrarSinModelo();
            }
            else MostrarAviso(clasificador.EsDemo ? "Modelo de demostración: los resultados aún no son reales." : "");

            if (captura) captura.Iniciar();
            if (captura && !string.IsNullOrEmpty(captura.Error)) MostrarAviso(captura.Error);
            if (bucle != null) StopCoroutine(bucle);
            bucle = StartCoroutine(Escanear());
        }

        void Cerrar()
        {
            if (bucle != null) { StopCoroutine(bucle); bucle = null; }
            if (captura) captura.Detener();
        }

        void Update()
        {
            if (lineaEscaneo == null || !lineaEscaneo.gameObject.activeInHierarchy) return;
            // Linea que sube y baja dentro del marco
            fase += Time.deltaTime * 0.55f;
            float t = Mathf.PingPong(fase, 1f);
            t = t * t * (3 - 2 * t);
            lineaEscaneo.anchoredPosition = new Vector2(0, Mathf.Lerp(-altoMarco * 0.42f, altoMarco * 0.42f, t));
        }

        IEnumerator Escanear()
        {
            var espera = new WaitForSeconds(intervalo);
            while (true)
            {
                yield return espera;
                if (clasificador == null || !clasificador.Disponible || captura == null) continue;
                if (!captura.TomarCuadro(out var px, out int w, out int h, out int giro)) continue;

                int s = clasificador.Tamano;
                if (pxMini == null || pxMini.Length != s * s) pxMini = new Color32[s * s];
                var tensor = ClasificadorEspecies.Preprocesar(px, w, h, giro, s, pxMini);
                ActualizarMiniatura(s);

                var tarea = clasificador.Inferir(tensor);
                while (!tarea.IsCompleted) yield return null;
                var logits = tarea.Result;
                if (logits == null) continue;

                var p = ClasificadorEspecies.Softmax(logits, clasificador.Meta.temperature);
                if (acumulado == null || acumulado.Length != p.Length) acumulado = p;
                else for (int i = 0; i < p.Length; i++) acumulado[i] = alfa * p[i] + (1 - alfa) * acumulado[i];

                Mostrar(clasificador.Decidir(acumulado));
            }
        }

        void ActualizarMiniatura(int s)
        {
            if (miniatura == null) return;
            if (texMini == null || texMini.width != s)
            {
                if (texMini) Destroy(texMini);
                texMini = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            }
            texMini.SetPixels32(pxMini);
            texMini.Apply(false);
            miniatura.texture = texMini;
            miniatura.color = Color.white;
        }

        // ------------------------------------------------------------------
        // Presentacion
        // ------------------------------------------------------------------
        void Mostrar(DecisionVision d)
        {
            ultima = d;
            var m = d.Mejor;
            var f = m.ficha;
            bool ok = d.tipo == TipoDecision.Identificada;
            Color c = ok ? colorIdentificado : d.tipo == TipoDecision.Incierta && m.prob > 0.35f ? colorDuda : colorNormal;
            foreach (var e in esquinas) if (e) e.color = Color.Lerp(e.color, c, 0.6f);

            if (d.tipo == TipoDecision.Incierta && m.prob < 0.35f)
            {
                MostrarBuscando();
                return;
            }

            string nombre = f != null ? f.comun : m.etiqueta.scientificName;
            if (textoTipo) textoTipo.text = d.tipo == TipoDecision.Demo ? "DEMO" : (f?.tipo ?? "Especie").ToUpperInvariant();
            if (textoNombre) textoNombre.text = ok ? nombre : d.tipo == TipoDecision.Demo ? nombre : "¿" + nombre + "?";
            if (textoCientifico) textoCientifico.text = $"<i>{m.etiqueta.scientificName}</i>";
            if (barra) barra.SetActive(true);
            if (rellenoBarra) rellenoBarra.anchorMax = new Vector2(Mathf.Clamp01(m.prob), 1);
            if (imagenRelleno) imagenRelleno.color = c == colorNormal ? new Color(0.16f, 0.52f, 1f) : c;
            if (textoConfianza) textoConfianza.text = $"{Mathf.RoundToInt(m.prob * 100)} %";

            if (textoDescripcion)
            {
                if (ok) textoDescripcion.text = Descripcion(f);
                else if (d.tipo == TipoDecision.Demo) textoDescripcion.text = "Este modelo es de prueba y no está entrenado: no confíes en el resultado.";
                else
                {
                    var alt = new System.Text.StringBuilder("No estoy seguro. Podría ser: ");
                    for (int i = 0; i < d.candidatas.Count; i++)
                    {
                        if (i > 0) alt.Append(", ");
                        var ci = d.candidatas[i];
                        alt.Append($"{(ci.ficha != null ? ci.ficha.comun : ci.etiqueta.scientificName)} ({Mathf.RoundToInt(ci.prob * 100)} %)");
                    }
                    alt.Append(". Acércate un poco más y mantén el celular quieto.");
                    textoDescripcion.text = alt.ToString();
                }
            }
            if (botonPreguntar) botonPreguntar.gameObject.SetActive(ok || d.tipo == TipoDecision.Demo);

            if (etiqueta) etiqueta.SetActive(ok);
            if (textoEtiqueta && ok) textoEtiqueta.text = $"<b>{nombre}</b>\n<size=70%><i>{m.etiqueta.scientificName}</i></size>";
            if (textoAyuda) textoAyuda.text = ok ? "¡Identificado!" : "Apunta la cámara a una planta o animal";

            // Frai lo dice en voz alta una sola vez por especie
            if (ok && ultimaHablada != m.etiqueta.scientificName)
            {
                ultimaHablada = m.etiqueta.scientificName;
                VozFactory.Instancia.Hablar($"¡Mira! Parece {Articulo(f)} {nombre}.");
            }
        }

        static string Descripcion(FichaEspecie f)
        {
            if (f == null || string.IsNullOrEmpty(f.descripcion)) return "Aún no tengo una ficha de esta especie. Pregúntale al guardaparque.";
            string t = f.descripcion;
            if (!string.IsNullOrEmpty(f.cuidado)) t += $"\n<color=#FFCB29>{f.cuidado}</color>";
            return t;
        }

        static string Articulo(FichaEspecie f)
        {
            string n = f?.comun ?? "";
            return n.StartsWith("Oso") || n.StartsWith("Cóndor") || n.StartsWith("Frailejón") || n.StartsWith("Chivito") || n.StartsWith("Guardacaminos") ? "un" : "una";
        }

        void MostrarBuscando()
        {
            if (textoTipo) textoTipo.text = "BUSCANDO";
            if (textoNombre) textoNombre.text = "Buscando especies…";
            if (textoCientifico) textoCientifico.text = "";
            if (barra) barra.SetActive(false);
            if (textoDescripcion) textoDescripcion.text = "Acerca la cámara a una planta, ave o animal y mantén el celular quieto.";
            if (botonPreguntar) botonPreguntar.gameObject.SetActive(false);
            if (etiqueta) etiqueta.SetActive(false);
            if (textoAyuda) textoAyuda.text = "Apunta la cámara a una planta o animal";
            foreach (var e in esquinas) if (e) e.color = colorNormal;
        }

        void MostrarSinModelo()
        {
            if (textoTipo) textoTipo.text = "NO DISPONIBLE";
            if (textoNombre) textoNombre.text = "Visión no disponible";
            if (textoDescripcion) textoDescripcion.text = clasificador ? clasificador.Motivo : "";
        }

        void MostrarAviso(string t)
        {
            if (aviso) aviso.SetActive(!string.IsNullOrEmpty(t));
            if (textoAviso) textoAviso.text = t;
        }

        void Preguntar()
        {
            if (ultima == null || guia == null) return;
            var m = ultima.Mejor;
            string nombre = m.ficha != null ? m.ficha.comun : m.etiqueta.scientificName;
            navegacion.Ir(NavegacionUI.Guia);
            guia.Preguntar($"¿Qué es el {nombre.ToLowerInvariant()}?");
        }
    }
}
