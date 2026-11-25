using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace GuiaFrailejon
{
    /// <summary>
    /// Cuando HAY senal (Manizales, hotel, entrada del parque) descarga la base de conocimiento mas reciente.
    /// El modelo de IA (.gguf) se descarga con la opcion "Download on Start" del componente LLM de
    /// LLM for Unity, o se incluye en la build (APK/AAB con Play Asset Delivery).
    /// </summary>
    public class ContentUpdater : MonoBehaviour
    {
        [Tooltip("URL publica del JSON oficial (p. ej. GitHub Pages o el sitio del proyecto).")]
        public string urlBaseConocimiento = "";
        public KnowledgeBase kb;

        IEnumerator Start()
        {
            if (string.IsNullOrEmpty(urlBaseConocimiento)) yield break;
            if (Application.internetReachability == NetworkReachability.NotReachable) yield break;

            using (var req = UnityWebRequest.Get(urlBaseConocimiento))
            {
                req.timeout = 15;
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success) { Debug.Log("[Update] Sin actualizacion: " + req.error); yield break; }

                string json = req.downloadHandler.text;
                DatosConocimiento nuevo = null;
                try { nuevo = JsonUtility.FromJson<DatosConocimiento>(json); } catch (Exception) { }
                // Solo se reemplaza si el archivo es valido y trae seguridad: nunca dejar al visitante sin protocolos.
                if (nuevo == null || nuevo.seguridad == null || nuevo.seguridad.Length == 0) { Debug.LogWarning("[Update] JSON descartado"); yield break; }
                if (kb.Datos != null && nuevo.version == kb.Datos.version) yield break;

                string tmp = KnowledgeBase.RutaActualizada + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(KnowledgeBase.RutaActualizada)) File.Delete(KnowledgeBase.RutaActualizada);
                File.Move(tmp, KnowledgeBase.RutaActualizada);
                Debug.Log("[Update] Base actualizada a v" + nuevo.version);
                yield return kb.Cargar();
            }
        }
    }
}
