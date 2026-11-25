namespace GuiaFrailejon
{
    /// <summary>
    /// Primer filtro: si la pregunta toca seguridad, se responde SOLO con el texto fijo validado.
    /// La IA generativa nunca redacta respuestas de seguridad.
    /// </summary>
    public class SafetyRouter
    {
        readonly EntradaSeguridad[] entradas;
        public SafetyRouter(EntradaSeguridad[] entradas) { this.entradas = entradas ?? new EntradaSeguridad[0]; }

        public EntradaSeguridad Detectar(string pregunta)
        {
            string q = TextNormalizer.Limpiar(pregunta);   // ya viene con espacios al inicio y al final
            EntradaSeguridad mejor = null; int mejorLargo = 0;
            foreach (var e in entradas)
            {
                if (e.palabras_clave == null) continue;
                foreach (var k in e.palabras_clave)
                {
                    string kk = TextNormalizer.Limpiar(k);  // " mal de altura "
                    if (kk.Trim().Length == 0) continue;
                    // Coincidencia por palabra completa; la clave mas larga gana ("mal de altura" > "altura").
                    if (kk.Length > mejorLargo && Coincide(q, kk)) { mejor = e; mejorLargo = kk.Length; }
                }
            }
            return mejor;
        }

        // Frases de varias palabras: coincidencia exacta. Una sola palabra (5+ letras): tambien por raiz,
        // para que "mareo" detecte "mareado"/"mareando". Ante la duda, es mejor dar el protocolo de seguridad.
        static bool Coincide(string q, string kk)
        {
            if (q.Contains(kk)) return true;
            string w = kk.Trim();
            if (w.Contains(" ") || w.Length < 5) return false;
            string raiz = w.Substring(0, w.Length - 1);
            foreach (var palabra in q.Split(' '))
                if (palabra.Length >= raiz.Length && palabra.StartsWith(raiz)) return true;
            return false;
        }
    }
}
