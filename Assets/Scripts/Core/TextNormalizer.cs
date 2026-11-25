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
            "puedo","puede","quiero","saber","favor","porfa","oye","hola",
            // Relleno de conversacion (sobre todo al hablar): no aportan nada a la busqueda.
            "buenas","buenos","buen","dias","tardes","noches","gracias","disculpa","disculpe","perdon","pregunta",
            "preguntar","preguntarte","quisiera","queria","podrias","podria","puedes","dime","decir","decirme",
            "cuentame","cuentas","contar","contarme","explicame","explicar","sabes","sabias","frai","estas","estan",
            "tan","algo","alguna","algun","ahi","pues","bueno","entonces","ustedes","usted","nos","ser",
            "hay","tiene","tienen","tengo","hacer","haces","seria","sobre","acerca","aca","aqui"
        };

        // Sinonimos sencillos: la palabra de la izquierda se busca como la de la derecha.
        static readonly Dictionary<string, string> Sinonimos = new Dictionary<string, string>
        {
            {"alto","altura"},{"alta","altura"},{"altitud","altura"},{"metros","altura"},
            {"fauna","animales"},{"bichos","animales"},{"aves","pajaros"},{"flora","plantas"},{"vegetacion","plantas"},
            {"camping","acampar"},{"carpa","acampar"},{"precio","cuesta"},{"valor","cuesta"},{"vale","cuesta"},
            {"cobran","cuesta"},{"tarifa","cuesta"},{"horario","hora"},{"abre","abren"},{"cierra","abren"},
            {"comida","comer"},{"perro","mascotas"},{"perros","mascotas"},{"mascota","mascotas"},
            {"volcanes","nevados"},{"volcan","nevados"},{"nieve","nevados"},
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
            foreach (var t0 in Limpiar(texto).Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries))
            {
                if (Vacias.Contains(t0)) continue;
                string t = Sinonimos.TryGetValue(t0, out var s) ? s : t0;
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
