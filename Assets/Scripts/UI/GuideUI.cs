using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GuiaFrailejon
{
    /// <summary>
    /// Interfaz movil del guia:
    /// - Barra superior: estacion, progreso del recorrido, mapita y boton de emergencia.
    /// - Panel inferior: conversacion en burbujas, preguntas sugeridas (chips), campo de texto y microfono.
    /// - Botones flotantes: silenciar y "Acompañame".
    /// </summary>
    public class GuideUI : MonoBehaviour
    {
        [Header("Logica")]
        public GuideBrain cerebro;
        public KnowledgeBase kb;
        public FrailejonSpawner spawner;          // null en modo sin AR
        public ARAvailability disponibilidadAR;
        public FraiAcompanante acompanante;
        public ModoSinAR modoSinAR;
        public GeoLocalizador geo;
        [Tooltip("Componente WhisperSTT (voz a texto). Opcional.")]
        public MonoBehaviour sttComponente;

        [Header("Barra superior")]
        public TMP_Text textoEstacion;
        public TMP_Text textoProgreso;
        public MiniMapa mapa;
        public Button botonEmergencia;

        [Header("Conversacion")]
        public ChatUI chat;
        public Transform contenedorChips;
        public Button prefabChip;
        public int maxSugeridas = 6;
        public TMP_InputField campoPregunta;
        public Button botonEnviar;
        public BotonMantener botonMic;
        [Tooltip("Texto de estado de la voz (Escuchando..., Entendiendo...). Se oculta cuando esta vacio.")]
        public TMP_Text textoMic;
        [Tooltip("Pastilla que contiene textoMic (opcional).")]
        public GameObject estadoVoz;

        [Header("Plegar el panel (para ver mejor a Frai en AR)")]
        public RectTransform panelInferior;
        public GameObject[] ocultablesAlPlegar;
        public Button botonPlegar;
        [Tooltip("Flecha que gira al plegar (opcional).")]
        public RectTransform iconoPlegar;
        public float altoPanelAbierto = 780, altoPanelPlegado = 330;
        bool plegado;

        [Header("Botones flotantes")]
        public Button botonSilenciar;
        public Button botonAcompanar;
        public TMP_Text textoAcompanar;
        public Button botonVenAqui;

        public event Action<string> AlCambiarEstacion;

        string estacionId = "";
        bool ocupado, transcribiendo;
        float proximoRefresco;
        readonly List<GameObject> chips = new List<GameObject>();
        ITextToSpeech voz;
        ISpeechToText stt;

        const string MicNormal = "";
        const string TituloInicial = "<size=72%><color=#C2DBFF>Guía del páramo</color></size>\nFrai";

        void Start()
        {
            voz = VozFactory.Instancia;
            stt = sttComponente as ISpeechToText;

            botonEnviar.onClick.AddListener(() => Enviar(campoPregunta.text));
            campoPregunta.onSubmit.AddListener(t => Enviar(t));
            botonEmergencia.onClick.AddListener(() => Enviar("emergencia"));
            if (botonSilenciar) botonSilenciar.onClick.AddListener(() => voz.Detener());
            if (botonAcompanar) botonAcompanar.onClick.AddListener(AlternarAcompanante);
            if (botonVenAqui) botonVenAqui.onClick.AddListener(() => { if (acompanante) acompanante.VenAqui(); });
            if (botonPlegar) botonPlegar.onClick.AddListener(AlternarPanel);
            if (botonMic)
            {
                botonMic.alPresionar.AddListener(EmpezarVoz);
                botonMic.alSoltar.AddListener(TerminarVoz);
            }
            if (spawner) spawner.AlDetectarEstacion += (e, _) => SeleccionarEstacion(e.id);
            if (mapa) mapa.AlTocarEstacion += i => { if (kb.Datos != null && i < kb.Datos.estaciones.Length) SeleccionarEstacion(kb.Datos.estaciones[i].id); };
            if (geo) geo.AlEntrarEstacion += AlLlegarPorGPS;

            textoEstacion.text = TituloInicial;
            textoProgreso.text = "Funciona sin internet";
            EstadoMic(MicNormal);
            Invoke(nameof(MensajeInicial), 1.5f);   // espera a saber si hay AR
        }

        void MensajeInicial()
        {
            if (!string.IsNullOrEmpty(estacionId)) return;
            bool ar = disponibilidadAR == null || disponibilidadAR.ModoAR;
            chat.Agregar(ar
                ? "¡Hola! Soy Frai. Apunta la cámara al letrero de la estación y apareceré para guiarte."
                : "¡Hola! Soy Frai. Tu celular no tiene realidad aumentada, pero igual te acompaño. Toca una estación en el mapa de arriba.",
                ChatUI.Estilo.Frai);
        }

        void Update()
        {
            if (Time.time < proximoRefresco) return;
            proximoRefresco = Time.time + 1f;

            // Microfono: visible solo si la voz esta instalada.
            if (botonMic)
            {
                bool hayVoz = stt != null && (stt.Disponible || stt.Cargando);
                botonMic.gameObject.SetActive(hayVoz);
                if (hayVoz && !stt.Grabando && !transcribiendo) EstadoMic(stt.Cargando ? "Cargando la voz…" : MicNormal);
            }

            // Boton acompañar: solo con AR y despues de ver un letrero.
            if (botonAcompanar)
            {
                bool ar = disponibilidadAR == null || disponibilidadAR.ModoAR;
                botonAcompanar.gameObject.SetActive(acompanante != null && ar && !string.IsNullOrEmpty(estacionId));
            }
            if (botonVenAqui) botonVenAqui.gameObject.SetActive(acompanante != null && acompanante.Activo);

            ActualizarProgreso();
        }

        // ------------------------------------------------------------------
        // Estaciones
        // ------------------------------------------------------------------
        /// <summary>Lo llaman el letrero (AR), el mapa o el GPS.</summary>
        public void SeleccionarEstacion(string id)
        {
            if (kb == null || kb.Datos == null) return;
            var est = Array.Find(kb.Datos.estaciones, e => e.id == id);
            if (est == null || id == estacionId) return;
            estacionId = id;

            int num = Array.IndexOf(kb.Datos.estaciones, est) + 1;
            string corto = !string.IsNullOrEmpty(est.corto) ? est.corto : KnowledgeBase.EsPendiente(est.nombre) ? id : est.nombre;
            textoEstacion.text = $"<size=72%><color=#C2DBFF>Estación {num}</color></size>\n{corto}";
            if (mapa) mapa.MarcarActual(id);
            if (modoSinAR) modoSinAR.MostrarEstacion(id);
            AlCambiarEstacion?.Invoke(id);

            Mostrar(new RespuestaGuia { tipo = TipoRespuesta.Oficial, texto = est.saludo });
            CrearChips();
            ActualizarProgreso();
        }

        void AlLlegarPorGPS(Estacion e)
        {
            bool ar = disponibilidadAR != null && disponibilidadAR.ModoAR;
            if (!ar || string.IsNullOrEmpty(estacionId)) SeleccionarEstacion(e.id);
            else if (e.id != estacionId)
                chat.Agregar($"Te estás acercando a {e.nombre}. Busca el letrero para que te cuente más.", ChatUI.Estilo.Nota);
        }

        void ActualizarProgreso()
        {
            if (kb == null || kb.Datos == null || string.IsNullOrEmpty(estacionId)) return;
            int i = Array.FindIndex(kb.Datos.estaciones, e => e.id == estacionId);
            string t = $"Estación {i + 1} de {kb.Datos.estaciones.Length}  ·  Sin internet";
            if (mapa && !string.IsNullOrEmpty(mapa.TextoDistancia)) t += "  ·  " + mapa.TextoDistancia;
            if (cerebro && cerebro.NivelActual == Nivel.Completo) t += "  ·  IA local";
            textoProgreso.text = t;
        }

        void CrearChips()
        {
            foreach (var go in chips) Destroy(go);
            chips.Clear();
            if (contenedorChips == null || prefabChip == null) return;

            var lista = kb.PreguntasDeEstacion(estacionId);
            lista.Sort((a, b) => (a.estacion == "*").CompareTo(b.estacion == "*"));   // primero las de la estacion
            int n = 0;
            foreach (var p in lista)
            {
                if (n >= maxSugeridas) break;
                if (p.preguntas == null || p.preguntas.Length == 0 || KnowledgeBase.EsPendiente(p.respuesta)) continue;
                n++;
                string q = p.preguntas[0];
                var b = Instantiate(prefabChip, contenedorChips);
                b.GetComponentInChildren<TMP_Text>().text = "¿" + char.ToUpper(q[0]) + q.Substring(1) + "?";
                b.onClick.AddListener(() => Enviar(q));
                chips.Add(b.gameObject);
            }
        }

        // ------------------------------------------------------------------
        // Preguntas
        // ------------------------------------------------------------------
        /// <summary>Hace una pregunta a Frai desde otra pantalla (Explorar, Clima...).</summary>
        public void Preguntar(string pregunta) => Enviar(pregunta);

        /// <summary>Agrega un aviso al chat sin preguntar nada.</summary>
        public void Nota(string texto, bool seguridad = false) =>
            chat.Agregar(texto, seguridad ? ChatUI.Estilo.Seguridad : ChatUI.Estilo.Nota);

        async void Enviar(string pregunta)
        {
            if (ocupado || string.IsNullOrWhiteSpace(pregunta)) return;
            ocupado = true;
            botonEnviar.interactable = false;
            voz.Detener();
            campoPregunta.text = "";

            if (plegado) AlternarPanel();
            chat.Agregar(pregunta.Trim(), ChatUI.Estilo.Usuario);
            var burbuja = chat.Agregar("Frai está pensando...", ChatUI.Estilo.Frai);

            var r = await cerebro.Preguntar(pregunta, estacionId, parcial => chat.Actualizar(burbuja, parcial));
            Mostrar(r, burbuja);

            botonEnviar.interactable = true;
            ocupado = false;
        }

        void Mostrar(RespuestaGuia r, ChatUI.Burbuja burbuja = null)
        {
            var estilo = r.tipo == TipoRespuesta.Seguridad ? ChatUI.Estilo.Seguridad : ChatUI.Estilo.Frai;
            string texto = r.texto;
            if (r.tipo == TipoRespuesta.Seguridad) texto = "<b>SEGURIDAD</b>\n" + texto;
            if (r.tipo == TipoRespuesta.GeneradaConFuente) texto = "<size=75%><i>Redactado por IA con información oficial</i></size>\n" + texto;
            string fuente = string.IsNullOrEmpty(r.fuente) || KnowledgeBase.EsPendiente(r.fuente) ? null : r.fuente;

            if (burbuja != null) chat.Actualizar(burbuja, texto, fuente, estilo);
            else chat.Agregar(texto, estilo, fuente);

            if (r.PuedeLeerse) voz.Hablar(r.texto);
        }

        // ------------------------------------------------------------------
        // Voz
        // ------------------------------------------------------------------
        void EmpezarVoz()
        {
            if (stt == null || !stt.Disponible || ocupado) return;
            voz.Detener();
            stt.Empezar();
            EstadoMic(stt.Grabando ? "Escuchando… suelta para enviar" : "Permite el uso del micrófono");
        }

        async void TerminarVoz()
        {
            if (stt == null || !stt.Grabando) return;
            transcribiendo = true;
            EstadoMic("Entendiendo…");
            string texto = await stt.Terminar();
            transcribiendo = false;
            EstadoMic(MicNormal);
            if (string.IsNullOrWhiteSpace(texto))
                chat.Agregar("No te escuché bien. Mantén presionado el botón mientras hablas, cerca del celular.", ChatUI.Estilo.Nota);
            else
                Enviar(texto);
        }

        void EstadoMic(string t)
        {
            if (textoMic) textoMic.text = t;
            if (estadoVoz) estadoVoz.SetActive(!string.IsNullOrEmpty(t));
        }

        void AlternarPanel()
        {
            plegado = !plegado;
            if (panelInferior) panelInferior.sizeDelta = new Vector2(panelInferior.sizeDelta.x, plegado ? altoPanelPlegado : altoPanelAbierto);
            if (ocultablesAlPlegar != null) foreach (var go in ocultablesAlPlegar) if (go) go.SetActive(!plegado);
            var t = botonPlegar ? botonPlegar.GetComponentInChildren<TMP_Text>() : null;
            if (t) t.text = plegado ? "Ver chat" : "Ocultar chat";
            if (iconoPlegar) iconoPlegar.localEulerAngles = new Vector3(0, 0, plegado ? 180 : 0);
        }

        // ------------------------------------------------------------------
        // Acompañante
        // ------------------------------------------------------------------
        void AlternarAcompanante()
        {
            if (acompanante == null) return;
            if (acompanante.Activo)
            {
                acompanante.Desactivar();
                if (textoAcompanar) textoAcompanar.text = "Acompáñame";
                chat.Agregar("Listo, me quedo en el letrero. Enfócalo para verme.", ChatUI.Estilo.Nota);
            }
            else
            {
                acompanante.Activar(spawner != null ? spawner.UltimaPosicion : null);
                if (spawner) spawner.OcultarMarcadores();
                if (textoAcompanar) textoAcompanar.text = "Quédate";
                chat.Agregar("¡Vamos juntos! Me quedo a tu lado mientras caminas. Si me pierdes de vista, sigue la flecha del borde o toca «Ven aquí».", ChatUI.Estilo.Nota);
            }
        }
    }
}
