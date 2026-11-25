using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;

namespace GuiaFrailejon
{
    public enum TipoRespuesta { Seguridad, Oficial, GeneradaConFuente, SinDato, Pendiente }

    public class RespuestaGuia
    {
        public TipoRespuesta tipo;
        public string texto, fuente;
        /// <summary>Solo se lee en voz alta si es informacion validada.</summary>
        public bool PuedeLeerse => tipo != TipoRespuesta.Pendiente;
    }

    /// <summary>
    /// Orquesta la respuesta:
    /// 1) Seguridad -> texto fijo validado (nunca IA).
    /// 2) Pregunta frecuente con alta coincidencia -> respuesta oficial tal cual.
    /// 3) Nivel completo -> el SLM redacta SOLO con los pasajes recuperados; si inventa cifras, se descarta.
    /// 4) Si nada sirve -> "no tengo ese dato oficial".
    /// </summary>
    public class GuideBrain : MonoBehaviour
    {
        public KnowledgeBase kb;
        [Tooltip("Arrastren aqui el LLMForUnityAdapter (opcional).")]
        public MonoBehaviour llmComponente;

        [Range(0, 1)] public float umbralRespuestaDirecta = 0.55f;
        [Range(0, 1)] public float umbralMinimo = 0.15f;
        public float tiempoMaxLLM = 30f;
        [Tooltip("SOLO PARA PRUEBAS: usa la IA aunque el celular tenga menos de 6 GB de RAM.")]
        public bool forzarIAParaPruebas = false;

        public const string MsgSinDato = "No tengo ese dato en la informacion oficial del parque. Pregunta al guardaparque de la estacion.";
        public const string MsgPendiente = "Esta informacion esta pendiente de validacion oficial.";

        SafetyRouter seguridad;
        Retriever buscador;
        ILocalLLM llm;
        public Nivel NivelActual { get; private set; } = Nivel.Basico;

        void Awake() { llm = llmComponente as ILocalLLM; }

        void OnEnable() { if (kb != null) kb.AlCargar += Inicializar; }
        void OnDisable() { if (kb != null) kb.AlCargar -= Inicializar; }
        void Start() { if (kb != null && kb.Listo) Inicializar(); }

        void Inicializar()
        {
            seguridad = new SafetyRouter(kb.Datos.seguridad);
            buscador = new Retriever(kb.Datos);
            ActualizarNivel();
            Debug.Log($"[Guia] Nivel: {NivelActual}  RAM: {SystemInfo.systemMemorySize} MB");
        }

        void ActualizarNivel()
        {
            bool hayIA = llm != null && llm.Disponible;
            NivelActual = (forzarIAParaPruebas && hayIA) ? Nivel.Completo : DeviceTier.Detectar(hayIA);
        }

        public async Task<RespuestaGuia> Preguntar(string pregunta, string estacionId, Action<string> alAvanzar = null)
        {
            if (buscador == null) Inicializar();
            ActualizarNivel();   // el modelo puede terminar de cargar despues del inicio
            if (string.IsNullOrWhiteSpace(pregunta)) return Sin();

            // 1) Seguridad
            var seg = seguridad.Detectar(pregunta);
            if (seg != null) return Validar(TipoRespuesta.Seguridad, seg.respuesta, seg.fuente);

            // 2) Busqueda
            var resultados = buscador.Buscar(pregunta, estacionId, 3);
            var mejor = resultados.FirstOrDefault();
            if (mejor == null || mejor.puntaje < umbralMinimo) return Sin();

            if (mejor.pasaje.esPregunta && mejor.puntaje >= umbralRespuestaDirecta)
                return Validar(TipoRespuesta.Oficial, mejor.pasaje.respuesta, mejor.pasaje.fuente);

            // 3) Nivel completo: el SLM redacta con contexto
            var utiles = resultados.Where(r => r.puntaje >= umbralMinimo && !KnowledgeBase.EsPendiente(r.pasaje.respuesta)).ToList();
            if (NivelActual == Nivel.Completo && llm != null && llm.Disponible && utiles.Count > 0)
            {
                string contexto = string.Join("\n", utiles.Select((r, i) => $"[{i + 1}] {r.pasaje.respuesta}"));
                try
                {
                    var tarea = llm.Generar(ConstruirPrompt(pregunta, contexto), alAvanzar);
                    var gano = await Task.WhenAny(tarea, Task.Delay(TimeSpan.FromSeconds(tiempoMaxLLM)));
                    if (gano == tarea)
                    {
                        string txt = (await tarea)?.Trim();
                        if (!string.IsNullOrEmpty(txt) && !txt.Contains("NO_SE") && CifrasRespaldadas(txt, contexto))
                            return new RespuestaGuia { tipo = TipoRespuesta.GeneradaConFuente, texto = txt, fuente = utiles[0].pasaje.fuente };
                        if (txt != null && txt.Contains("NO_SE")) return Sin();
                    }
                    else llm.Cancelar();
                }
                catch (Exception e) { Debug.LogWarning("[Guia] LLM fallo, se usa respuesta oficial: " + e.Message); }
            }

            // 4) Respaldo: la respuesta oficial mas cercana
            return Validar(TipoRespuesta.Oficial, mejor.pasaje.respuesta, mejor.pasaje.fuente);
        }

        static string ConstruirPrompt(string pregunta, string contexto)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Eres Frai, un frailejon que guia visitantes en el Parque Nacional Natural Los Nevados.");
            sb.AppendLine("Responde en espanol, en maximo 3 frases cortas y amables.");
            sb.AppendLine("USA SOLO la INFORMACION OFICIAL de abajo. No agregues datos, cifras, horarios ni nombres que no esten ahi.");
            sb.AppendLine("Si la informacion no responde la pregunta, responde exactamente: NO_SE");
            sb.AppendLine("No des consejos medicos ni de seguridad propios.");
            sb.AppendLine();
            sb.AppendLine("INFORMACION OFICIAL:");
            sb.AppendLine(contexto);
            sb.AppendLine();
            sb.AppendLine("PREGUNTA DEL VISITANTE: " + pregunta);
            sb.Append("RESPUESTA:");
            return sb.ToString();
        }

        /// <summary>Toda cifra de la respuesta generada debe aparecer en el contexto oficial.</summary>
        static bool CifrasRespaldadas(string respuesta, string contexto)
        {
            foreach (Match m in Regex.Matches(respuesta, @"\d+([.,]\d+)?"))
                if (!contexto.Contains(m.Value)) return false;
            return true;
        }

        static RespuestaGuia Validar(TipoRespuesta tipo, string texto, string fuente)
        {
            if (KnowledgeBase.EsPendiente(texto)) return new RespuestaGuia { tipo = TipoRespuesta.Pendiente, texto = MsgPendiente, fuente = fuente };
            return new RespuestaGuia { tipo = tipo, texto = texto, fuente = fuente };
        }

        static RespuestaGuia Sin() => new RespuestaGuia { tipo = TipoRespuesta.SinDato, texto = MsgSinDato };
    }
}
