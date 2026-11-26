using System;
using System.Collections;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace GuiaFrailejon
{
    /// <summary>Contenido de manifest.json: lo publica el portal del guia junto con cada version de la base.</summary>
    [Serializable]
    public class ManifestConocimiento
    {
        public string version;     // misma version que trae base_conocimiento.json
        public string archivo;     // ruta de la base, relativa al manifest (p. ej. "base_conocimiento.json")
        public string sha256;      // hash hexadecimal del archivo
        public long bytes;         // tamano esperado
    }

    /// <summary>
    /// Cuando HAY senal (Manizales, hotel, entrada del parque) descarga la base de conocimiento mas reciente.
    /// Flujo: lee manifest.json -> si la version es nueva, descarga la base -> verifica tamano y SHA-256 ->
    /// valida el JSON -> reemplaza la copia local de forma atomica. Cualquier fallo deja la base actual intacta.
    /// El QR del parque apunta a una URL fija; lo que cambia es el contenido publicado detras de ella.
    /// El modelo de IA (.gguf) se descarga con "Download on Start" de LLM for Unity o va en la build.
    /// </summary>
    public class ContentUpdater : MonoBehaviour
    {
        [Tooltip("URL publica del manifest.json (p. ej. https://.../conocimiento/manifest.json). Vacio = sin actualizaciones.")]
        public string urlBaseConocimiento = "";
        public KnowledgeBase kb;

        /// <summary>Permite al QR / deep link forzar la URL del manifest.</summary>
        public void UsarUrl(string url) { urlBaseConocimiento = url; StartCoroutine(Actualizar()); }

        IEnumerator Start() { yield return Actualizar(); }

        public IEnumerator Actualizar()
        {
            if (string.IsNullOrEmpty(urlBaseConocimiento)) yield break;
            if (Application.internetReachability == NetworkReachability.NotReachable) yield break;

            // 1) Manifest (pequeno).
            ManifestConocimiento m = null;
            using (var req = UnityWebRequest.Get(urlBaseConocimiento))
            {
                req.timeout = 15;
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success) { Debug.Log("[Update] Sin actualizacion: " + req.error); yield break; }
                try { m = JsonUtility.FromJson<ManifestConocimiento>(req.downloadHandler.text); } catch (Exception) { }
            }
            if (m == null || string.IsNullOrEmpty(m.archivo) || string.IsNullOrEmpty(m.sha256) || string.IsNullOrEmpty(m.version))
            { Debug.LogWarning("[Update] manifest invalido"); yield break; }

            if (kb != null && !kb.Listo) yield return new WaitUntil(() => kb.Listo);
            if (kb != null && kb.Datos != null && m.version == kb.Datos.version) yield break;   // ya esta al dia

            // 2) Base nueva (se resuelve relativa al manifest).
            string urlBase = new Uri(new Uri(urlBaseConocimiento), m.archivo).ToString();
            byte[] datos;
            using (var req = UnityWebRequest.Get(urlBase))
            {
                req.timeout = 60;
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success) { Debug.Log("[Update] Descarga fallida: " + req.error); yield break; }
                datos = req.downloadHandler.data;
            }

            // 3) Integridad: tamano y hash. Un archivo cortado o alterado se descarta.
            if (m.bytes > 0 && datos.LongLength != m.bytes) { Debug.LogWarning("[Update] tamano distinto al manifest, descartado"); yield break; }
            if (!HashCoincide(datos, m.sha256)) { Debug.LogWarning("[Update] hash no coincide, descartado"); yield break; }

            // 4) Contenido: JSON valido y con protocolos de seguridad (nunca dejar al visitante sin ellos).
            string json = new UTF8Encoding(false).GetString(datos);
            if (json.Length > 0 && json[0] == '﻿') json = json.Substring(1);
            DatosConocimiento nuevo = null;
            try { nuevo = JsonUtility.FromJson<DatosConocimiento>(json); } catch (Exception) { }
            if (nuevo == null || nuevo.seguridad == null || nuevo.seguridad.Length == 0) { Debug.LogWarning("[Update] JSON descartado"); yield break; }
            if (nuevo.version != m.version) { Debug.LogWarning("[Update] version del JSON distinta a la del manifest, descartado"); yield break; }

            // 5) Reemplazo atomico.
            string tmp = KnowledgeBase.RutaActualizada + ".tmp";
            File.WriteAllBytes(tmp, datos);
            if (File.Exists(KnowledgeBase.RutaActualizada)) File.Delete(KnowledgeBase.RutaActualizada);
            File.Move(tmp, KnowledgeBase.RutaActualizada);
            Debug.Log("[Update] Base actualizada a v" + nuevo.version);
            if (kb != null) yield return kb.Cargar();
        }

        static bool HashCoincide(byte[] datos, string esperado)
        {
            using (var sha = SHA256.Create())
            {
                var h = sha.ComputeHash(datos);
                var sb = new StringBuilder(h.Length * 2);
                foreach (var b in h) sb.Append(b.ToString("x2"));
                return string.Equals(sb.ToString(), esperado.Trim(), StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
