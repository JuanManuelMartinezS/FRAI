using System.Collections.Generic;
using System.Text;

namespace GuiaFrailejon
{
    /// <summary>Normaliza texto en espanol: minusculas, sin tildes, sin signos, sin palabras vacias.</summary>
    public static class TextNormalizer
    {
        static readonly HashSet<string> Vacias = new HashSet<string>
        {
            "el","la","los","las","un","una","unos","unas","de","del","al","a","en","y","o","u","que","por","para",
            "con","sin","se","me","te","le","lo","mi","tu","su","es","son","como","cual","cuales","donde",
            "cuando","esta","este","esto","estoy","eso","ese","esa","alla","muy","mas","ya","yo","si","no",
            "puedo","puede","quiero","saber","favor","porfa","oye","hola"
        };

        // Se mantienen "no", "si", "puedo" fuera del filtro de seguridad: SafetyRouter usa Limpiar() sin quitar vacias.
        public static string Limpiar(string texto)
        {
            if (string.IsNullOrEmpty(texto)) return "";
            var sb = new StringBuilder(texto.Length);
            foreach (char c0 in texto.ToLowerInvariant())
            {
                char c = c0;
                switch (c)
                {
                    case 'á': case 'à': case 'ä': c = 'a'; break;
                    case 'é': case 'è': case 'ë': c = 'e'; break;
                    case 'í': case 'ì': case 'ï': c = 'i'; break;
                    case 'ó': case 'ò': case 'ö': c = 'o'; break;
                    case 'ú': case 'ù': case 'ü': c = 'u'; break;
                    case 'ñ': c = 'n'; break;
                }
                sb.Append(char.IsLetterOrDigit(c) ? c : ' ');
            }
            return " " + string.Join(" ", sb.ToString().Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries)) + " ";
        }

        public static List<string> Tokens(string texto)
        {
            var res = new List<string>();
            foreach (var t in Limpiar(texto).Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries))
            {
                if (Vacias.Contains(t)) continue;
                res.Add(Raiz(t));
            }
            return res;
        }

        // Raiz muy simple: quita plurales para que "frailejones" == "frailejon".
        static string Raiz(string t)
        {
            if (t.Length > 5 && t.EndsWith("es")) return t.Substring(0, t.Length - 2);
            if (t.Length > 4 && t.EndsWith("s")) return t.Substring(0, t.Length - 1);
            return t;
        }
    }
}
