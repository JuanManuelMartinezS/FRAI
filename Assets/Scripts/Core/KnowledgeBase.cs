using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace GuiaFrailejon
{
    // ---- Modelo de datos (coincide con base_conocimiento.json) ----
    [Serializable]
    public class Estacion
    {
        public string id, nombre, corto, marcador, saludo;
        public double lat, lon;      // 0 = sin coordenadas (llenar en campo)
        public float radio_m = 60;   // distancia para considerar que se llego a la estacion
    }
    [Serializable] public class EntradaSeguridad { public string id; public string[] palabras_clave; public string respuesta, fuente; }
    [Serializable] public class EntradaPregunta { public string id, estacion; public string[] preguntas; public string respuesta, fuente; }
    [Serializable] public class Documento { public string id, titulo, texto, fuente; }

    [Serializable]
    public class DatosConocimiento
    {
        public string version, fecha_actualizacion;
        public Estacion[] estaciones;
        public EntradaSeguridad[] seguridad;
        public EntradaPregunta[] preguntas;
        public Documento[] documentos;
    }

    /// <summary>
    /// Carga la base de conocimiento oficial.
    /// Prioridad: copia actualizada en persistentDataPath (descargada con senal) -> copia incluida en la app (StreamingAssets).
    /// </summary>
    public class KnowledgeBase : MonoBehaviour
    {
        public const string NombreArchivo = "base_conocimiento.json";
        public static string RutaActualizada => Path.Combine(Application.persistentDataPath, NombreArchivo);

        public DatosConocimiento Datos { get; private set; }
        public bool Listo { get; private set; }
        public event Action AlCargar;

        IEnumerator Start() { yield return Cargar(); }

        public IEnumerator Cargar()
        {
            string json = null;

            if (File.Exists(RutaActualizada))
            {
                json = File.ReadAllText(RutaActualizada);
            }
            else
            {
                // En Android StreamingAssets esta dentro del APK: hay que leerlo con UnityWebRequest.
                string ruta = Path.Combine(Application.streamingAssetsPath, "conocimiento", NombreArchivo);
                if (!ruta.Contains("://")) ruta = "file://" + ruta;
                using (var req = UnityWebRequest.Get(ruta))
                {
                    yield return req.SendWebRequest();
                    if (req.result == UnityWebRequest.Result.Success) json = req.downloadHandler.text;
                    else Debug.LogError("[KB] No se pudo leer la base incluida: " + req.error);
                }
            }

            if (!string.IsNullOrEmpty(json))
            {
                try { Datos = JsonUtility.FromJson<DatosConocimiento>(json); }
                catch (Exception e) { Debug.LogError("[KB] JSON invalido: " + e.Message); }
            }

            if (Datos == null) Datos = new DatosConocimiento
            {
                estaciones = new Estacion[0], seguridad = new EntradaSeguridad[0],
                preguntas = new EntradaPregunta[0], documentos = new Documento[0]
            };

            Listo = true;
            Debug.Log($"[KB] Base v{Datos.version} ({Datos.fecha_actualizacion}) cargada.");
            AlCargar?.Invoke();
        }

        public Estacion BuscarEstacionPorMarcador(string marcador)
        {
            foreach (var e in Datos.estaciones) if (e.marcador == marcador) return e;
            return null;
        }

        public List<EntradaPregunta> PreguntasDeEstacion(string estacionId)
        {
            var lista = new List<EntradaPregunta>();
            foreach (var p in Datos.preguntas)
                if (p.estacion == "*" || p.estacion == estacionId) lista.Add(p);
            return lista;
        }

        /// <summary>Un texto sin validar nunca debe leerse al visitante.</summary>
        public static bool EsPendiente(string texto) =>
            string.IsNullOrWhiteSpace(texto) || texto.TrimStart().StartsWith("[PENDIENTE", StringComparison.OrdinalIgnoreCase);
    }
}
