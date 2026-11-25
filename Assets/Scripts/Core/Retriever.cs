using System;
using System.Collections.Generic;
using System.Linq;

namespace GuiaFrailejon
{
    public class Pasaje
    {
        public string id, texto, respuesta, fuente, estacion;
        public bool esPregunta;           // true = entrada de preguntas frecuentes, false = fragmento de documento
        public List<string> tokens;
    }

    public class Resultado { public Pasaje pasaje; public float puntaje; }

    /// <summary>
    /// Buscador BM25 sin dependencias. Corre en cualquier celular, no genera texto: solo encuentra
    /// la respuesta oficial mas parecida. Es el "nivel basico" y tambien el RAG del nivel completo.
    /// (Mejora futura: cambiar por embeddings, p. ej. el RAG incluido en LLM for Unity.)
    /// </summary>
    public class Retriever
    {
        const float k1 = 1.2f, b = 0.75f;
        const float PesoDesconocida = 0.25f;
        readonly List<Pasaje> pasajes = new List<Pasaje>();
        readonly Dictionary<string, int> df = new Dictionary<string, int>();
        float largoPromedio;

        public Retriever(DatosConocimiento datos)
        {
            foreach (var p in datos.preguntas ?? new EntradaPregunta[0])
            {
                // Las preguntas de ejemplo pesan doble frente a la respuesta.
                string texto = string.Join(" ", p.preguntas ?? new string[0]);
                pasajes.Add(new Pasaje
                {
                    id = p.id, texto = texto + " " + texto + " " + p.respuesta,
                    respuesta = p.respuesta, fuente = p.fuente, estacion = p.estacion, esPregunta = true
                });
            }
            foreach (var d in datos.documentos ?? new Documento[0])
                pasajes.Add(new Pasaje { id = d.id, texto = d.titulo + " " + d.texto, respuesta = d.texto, fuente = d.fuente, estacion = "*" });

            foreach (var p in pasajes)
            {
                p.tokens = TextNormalizer.Tokens(p.texto);
                foreach (var t in p.tokens.Distinct()) df[t] = df.TryGetValue(t, out var n) ? n + 1 : 1;
            }
            largoPromedio = pasajes.Count == 0 ? 1 : (float)pasajes.Average(p => p.tokens.Count);
        }

        /// <summary>Devuelve los mejores pasajes. Puntaje normalizado 0..1 (fraccion de terminos de la pregunta cubiertos, ponderada).</summary>
        public List<Resultado> Buscar(string pregunta, string estacionId, int top = 3)
        {
            var q = TextNormalizer.Tokens(pregunta).Distinct().ToList();
            var res = new List<Resultado>();
            if (q.Count == 0) return res;

            int N = pasajes.Count;
            // Las palabras que no aparecen en ninguna parte de la base (relleno, errores de la voz) pesan poco:
            // si contaran completo, una pregunta larga y natural nunca alcanzaria el umbral.
            float maxPosible = 0;
            foreach (var t in q) maxPosible += Idf(t, N) * (k1 + 1) * (df.ContainsKey(t) ? 1f : PesoDesconocida);

            foreach (var p in pasajes)
            {
                if (p.estacion != "*" && p.estacion != estacionId) continue;
                float s = 0;
                foreach (var t in q)
                {
                    int tf = p.tokens.Count(x => x == t);
                    if (tf == 0) continue;
                    float idf = Idf(t, N);
                    s += idf * (tf * (k1 + 1)) / (tf + k1 * (1 - b + b * p.tokens.Count / largoPromedio));
                }
                if (s > 0) res.Add(new Resultado { pasaje = p, puntaje = maxPosible > 0 ? Math.Min(1f, s / maxPosible) : 0 });
            }
            return res.OrderByDescending(r => r.puntaje).Take(top).ToList();
        }

        float Idf(string t, int N)
        {
            int n = df.TryGetValue(t, out var v) ? v : 0;
            return (float)Math.Log(1 + (N - n + 0.5) / (n + 0.5));
        }
    }
}
