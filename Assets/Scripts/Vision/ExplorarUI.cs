using System.Collections;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GuiaFrailejon
{
    /// <summary>
    /// Pestana "Explorar": identifica una especie SOLO cuando el usuario toma una foto o sube una de la galeria
    /// (no hay deteccion continua: ahorra bateria y evita resultados que parpadean).
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
        [Tooltip("Lado mayor (px) al que se reduce una foto subida de la galeria.")]
        public int tamanoMaxFoto = 1024;

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

        Texture2D texMini;
        Color32[] pxMini;
        DecisionVision ultima;
        string ultimaHablada;
        Coroutine analisis;
        float fase;
        bool procesando;
        Button botonFoto, botonSubir;
        const int NTop = 3;
        GameObject panelTop;
        readonly TMP_Text[] topNombre = new TMP_Text[NTop], topPct = new TMP_Text[NTop];
        readonly RectTransform[] topRelleno = new RectTransform[NTop];
        readonly Image[] topImgRelleno = new Image[NTop];

        void Start()
        {
            if (navegacion) navegacion.AlCambiar += i => { if (i == NavegacionUI.Explorar) Abrir(); else Cerrar(); };
            if (botonPreguntar) botonPreguntar.onClick.AddListener(Preguntar);
            if (botonVolver) botonVolver.onClick.AddListener(() => navegacion.Ir(NavegacionUI.Guia));
            CrearBotonesFoto();
        }

        /// <summary>Crea "Tomar foto" y "Subir foto" clonando el estilo de "Preguntar a Frai".</summary>
        void CrearBotonesFoto()
        {
            if (botonPreguntar == null) return;
            var padre = botonPreguntar.transform.parent.parent;   // la pestana Explorar (no la tarjeta)
            Button Clonar(string nombre, string texto, float x)
            {
                var b = Instantiate(botonPreguntar, padre);
                b.name = nombre;
                b.onClick.RemoveAllListeners();
                var ic = b.transform.Find("Icono");
                if (ic) Destroy(ic.gameObject);
                var t = b.GetComponentInChildren<TMP_Text>(true);
                if (t) t.text = texto;
                var rt = (RectTransform)b.transform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0);
                rt.anchoredPosition = new Vector2(x, 600);
                rt.sizeDelta = new Vector2(400, 96);
                b.gameObject.SetActive(true);
                return b;
            }
            botonFoto = Clonar("BtnTomarFoto", "Tomar foto", -215);
            botonSubir = Clonar("BtnSubirFoto", "Subir foto", 215);
            botonFoto.onClick.AddListener(TomarFoto);
            botonSubir.onClick.AddListener(SubirFoto);
            CrearTop3(padre);
        }

        /// <summary>Panel con las 3 especies mas probables (nombre, barra y porcentaje), encima de los botones.</summary>
        void CrearTop3(Transform padre)
        {
            var sprite = botonPreguntar.GetComponent<Image>()?.sprite;
            var fuente = textoNombre ? textoNombre.font : null;
            const float AltoFila = 36f, Margen = 8f;

            panelTop = new GameObject("Top3", typeof(RectTransform), typeof(Image));
            panelTop.transform.SetParent(padre, false);
            var prt = (RectTransform)panelTop.transform;
            prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0);
            prt.anchoredPosition = new Vector2(0, 706);
            prt.sizeDelta = new Vector2(940, NTop * AltoFila + 2 * Margen);
            var fondo = panelTop.GetComponent<Image>();
            fondo.sprite = sprite; fondo.type = sprite ? Image.Type.Sliced : Image.Type.Simple;
            fondo.color = new Color(0.04f, 0.09f, 0.18f, 0.82f);
            fondo.raycastTarget = false;

            for (int i = 0; i < NTop; i++)
            {
                var fila = new GameObject("Fila" + (i + 1), typeof(RectTransform));
                fila.transform.SetParent(panelTop.transform, false);
                var frt = (RectTransform)fila.transform;
                frt.anchorMin = new Vector2(0, 1); frt.anchorMax = new Vector2(1, 1); frt.pivot = new Vector2(0.5f, 1);
                frt.anchoredPosition = new Vector2(0, -(Margen + i * AltoFila));
                frt.sizeDelta = new Vector2(-32, AltoFila - 4);

                var pista = new GameObject("Pista", typeof(RectTransform), typeof(Image));
                pista.transform.SetParent(fila.transform, false);
                Estirar(pista);
                var ip = pista.GetComponent<Image>();
                ip.sprite = sprite; ip.type = sprite ? Image.Type.Sliced : Image.Type.Simple;
                ip.color = new Color(1, 1, 1, 0.10f); ip.raycastTarget = false;

                var relleno = new GameObject("Relleno", typeof(RectTransform), typeof(Image));
                relleno.transform.SetParent(pista.transform, false);
                topRelleno[i] = Estirar(relleno);
                topImgRelleno[i] = relleno.GetComponent<Image>();
                topImgRelleno[i].sprite = sprite; topImgRelleno[i].type = sprite ? Image.Type.Sliced : Image.Type.Simple;
                topImgRelleno[i].raycastTarget = false;

                topNombre[i] = CrearTexto(fila.transform, "Nombre", fuente, TextAlignmentOptions.Left, 22, 20, 120);
                topPct[i] = CrearTexto(fila.transform, "Pct", fuente, TextAlignmentOptions.Right, 22, 0, 20);
                var prt2 = (RectTransform)topPct[i].transform;
                prt2.anchorMin = new Vector2(1, 0); prt2.anchorMax = new Vector2(1, 1); prt2.pivot = new Vector2(1, 0.5f);
                prt2.sizeDelta = new Vector2(100, 0); prt2.anchoredPosition = new Vector2(-20, 0);
                topPct[i].fontStyle = FontStyles.Bold;
            }
            panelTop.SetActive(false);
        }

        static RectTransform Estirar(GameObject go)
        {
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
            r.offsetMin = r.offsetMax = Vector2.zero;
            return r;
        }

        static TMP_Text CrearTexto(Transform padre, string nombre, TMP_FontAsset fuente, TextAlignmentOptions alineacion,
                                   float tam, float izq, float der)
        {
            var go = new GameObject(nombre, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(padre, false);
            var r = Estirar(go);
            r.offsetMin = new Vector2(izq, 0); r.offsetMax = new Vector2(-der, 0);
            var t = go.GetComponent<TextMeshProUGUI>();
            if (fuente) t.font = fuente;
            t.fontSize = tam; t.color = Color.white; t.alignment = alineacion;
            t.textWrappingMode = TextWrappingModes.NoWrap; t.overflowMode = TextOverflowModes.Ellipsis;
            t.raycastTarget = false;
            return t;
        }

        void MostrarTop3(DecisionVision d, Color principal)
        {
            if (panelTop == null) return;
            panelTop.SetActive(d != null && d.candidatas.Count > 0);
            if (d == null) return;
            for (int i = 0; i < NTop; i++)
            {
                bool hay = i < d.candidatas.Count;
                topNombre[i].transform.parent.gameObject.SetActive(hay);
                if (!hay) continue;
                var c = d.candidatas[i];
                string nombre = c.ficha != null && !string.IsNullOrEmpty(c.ficha.comun) ? c.ficha.comun : c.etiqueta.scientificName;
                topNombre[i].text = $"{i + 1}.  {nombre}";
                topPct[i].text = c.prob < 0.01f ? "<1 %" : $"{Mathf.RoundToInt(c.prob * 100)} %";
                topRelleno[i].anchorMax = new Vector2(Mathf.Clamp(c.prob, 0.02f, 1f), 1);
                var col = i == 0 ? principal : new Color(0.16f, 0.52f, 1f);
                col.a = i == 0 ? 0.55f : 0.35f;
                topImgRelleno[i].color = col;
            }
        }

        void Abrir()
        {
            ultima = null;
            procesando = false;
            MostrarTop3(null, colorNormal);
            MostrarBuscando();
            if (clasificador == null || !clasificador.Disponible)
            {
                MostrarAviso(clasificador ? clasificador.Motivo : "Falta el clasificador de especies.");
                MostrarSinModelo();
            }
            else MostrarAviso(clasificador.EsDemo ? "Modelo de demostración: los resultados aún no son reales." : "");

            if (captura) captura.Iniciar();
            if (captura && !string.IsNullOrEmpty(captura.Error)) MostrarAviso(captura.Error);
            FijarBotones(true);
        }

        void Cerrar()
        {
            if (analisis != null) { StopCoroutine(analisis); analisis = null; }
            procesando = false;
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

        void FijarBotones(bool activos)
        {
            if (botonFoto) botonFoto.interactable = activos;
            if (botonSubir) botonSubir.interactable = activos;
        }

        // ------------------------------------------------------------------
        // Una sola identificacion por foto
        // ------------------------------------------------------------------
        void TomarFoto()
        {
            if (procesando) return;
            if (clasificador == null || !clasificador.Disponible)
            {
                MostrarAviso(clasificador ? clasificador.Motivo : "Falta el clasificador de especies.");
                return;
            }
            if (captura == null || !captura.TomarCuadro(out var px, out int w, out int h, out int giro))
            {
                MostrarAviso("La cámara aún no está lista. Espera un segundo e inténtalo de nuevo.");
                return;
            }
            MostrarAviso("");
            Analizar(px, w, h, giro);
        }

        void SubirFoto()
        {
            if (procesando) return;
            if (clasificador == null || !clasificador.Disponible)
            {
                MostrarAviso(clasificador ? clasificador.Motivo : "Falta el clasificador de especies.");
                return;
            }
#if UNITY_EDITOR
            string ruta = UnityEditor.EditorUtility.OpenFilePanel("Elige una foto", "", "jpg,jpeg,png");
            if (string.IsNullOrEmpty(ruta)) return;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(File.ReadAllBytes(ruta))) { Destroy(tex); MostrarAviso("No pude leer esa imagen."); return; }
            AnalizarTextura(tex);
#elif GUIA_GALERIA
            NativeGallery.GetImageFromGallery(ruta =>
            {
                if (string.IsNullOrEmpty(ruta)) return;
                var tex = NativeGallery.LoadImageAtPath(ruta, tamanoMaxFoto, false, false);
                if (tex == null) { MostrarAviso("No pude leer esa imagen."); return; }
                AnalizarTextura(tex);
            }, "Elige una foto", "image/*");
#else
            MostrarAviso("Subir foto no está disponible: falta el paquete NativeGallery.");
#endif
        }

        void AnalizarTextura(Texture2D tex)
        {
            MostrarAviso("");
            var px = tex.GetPixels32();
            int w = tex.width, h = tex.height;
            Destroy(tex);
            Analizar(px, w, h, 0);   // LoadImageAtPath ya aplica la orientacion EXIF
        }

        void Analizar(Color32[] px, int w, int h, int giro)
        {
            if (analisis != null) StopCoroutine(analisis);
            analisis = StartCoroutine(Clasificar(px, w, h, giro));
        }

        IEnumerator Clasificar(Color32[] px, int w, int h, int giro)
        {
            procesando = true;
            FijarBotones(false);
            MostrarTop3(null, colorNormal);
            if (textoTipo) textoTipo.text = "ANALIZANDO";
            if (textoNombre) textoNombre.text = "Analizando…";
            if (textoCientifico) textoCientifico.text = "";
            if (barra) barra.SetActive(false);
            if (botonPreguntar) botonPreguntar.gameObject.SetActive(false);
            if (etiqueta) etiqueta.SetActive(false);
            yield return null;   // deja pintar el "Analizando…"

            int s = clasificador.Tamano;
            if (pxMini == null || pxMini.Length != s * s) pxMini = new Color32[s * s];
            var tensor = ClasificadorEspecies.Preprocesar(px, w, h, giro, s, pxMini,
                clasificador.Meta.input.mean, clasificador.Meta.input.std, clasificador.Meta.input.cropPct);
            ActualizarMiniatura(s);

            var tarea = clasificador.Inferir(tensor);
            while (!tarea.IsCompleted) yield return null;
            var logits = tarea.Result;

            procesando = false;
            analisis = null;
            FijarBotones(true);
            if (logits == null) { MostrarNoReconocido("No pude analizar la foto. Inténtalo otra vez."); yield break; }

            var p = ClasificadorEspecies.Softmax(logits, clasificador.Meta.temperature);
            Mostrar(clasificador.Decidir(p));
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
            MostrarTop3(d, c == colorNormal ? new Color(0.16f, 0.52f, 1f) : c);

            if (d.tipo == TipoDecision.Incierta && m.prob < 0.35f)
            {
                MostrarNoReconocido("Acércate más, busca buena luz y toma otra foto, o sube una más clara.");
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
                    alt.Append(". Prueba con otra foto más cerca y con buena luz.");
                    textoDescripcion.text = alt.ToString();
                }
            }
            if (botonPreguntar) botonPreguntar.gameObject.SetActive(ok || d.tipo == TipoDecision.Demo);

            if (etiqueta) etiqueta.SetActive(ok);
            if (textoEtiqueta && ok) textoEtiqueta.text = $"<b>{nombre}</b>\n<size=70%><i>{m.etiqueta.scientificName}</i></size>";
            if (textoAyuda) textoAyuda.text = ok ? "¡Identificado!" : "Toca «Tomar foto» para intentar de nuevo";

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
            if (textoTipo) textoTipo.text = "LISTO";
            if (textoNombre) textoNombre.text = "Toma o sube una foto";
            if (textoCientifico) textoCientifico.text = "";
            if (barra) barra.SetActive(false);
            if (textoDescripcion) textoDescripcion.text = "Apunta a una planta, ave o animal y toca «Tomar foto», o sube una foto de tu galería.";
            if (botonPreguntar) botonPreguntar.gameObject.SetActive(false);
            if (etiqueta) etiqueta.SetActive(false);
            if (textoAyuda) textoAyuda.text = "Apunta la cámara a una planta o animal";
            foreach (var e in esquinas) if (e) e.color = colorNormal;
        }

        void MostrarNoReconocido(string consejo)
        {
            MostrarBuscando();
            if (textoTipo) textoTipo.text = "SIN RESULTADO";
            if (textoNombre) textoNombre.text = "No pude identificarlo";
            if (textoDescripcion) textoDescripcion.text = consejo;
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
