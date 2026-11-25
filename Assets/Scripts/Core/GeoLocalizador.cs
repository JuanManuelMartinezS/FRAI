using System;
using System.Collections;
using UnityEngine;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

namespace GuiaFrailejon
{
    /// <summary>
    /// GPS del celular (funciona en modo avion: solo recibe senal de satelites).
    /// Detecta a que estacion esta cerca el visitante usando lat / lon / radio_m de base_conocimiento.json.
    /// Si las coordenadas estan en 0 (sin llenar), no detecta estaciones: el visitante las elige en el mapa o con el letrero.
    /// </summary>
    public class GeoLocalizador : MonoBehaviour
    {
        public KnowledgeBase kb;
        public float precisionDeseadaM = 10f;
        public float distanciaActualizacionM = 5f;
        [Tooltip("Radio minimo para considerar que el visitante llego a una estacion.")]
        public float radioMinimoM = 30f;

        public bool Activo { get; private set; }
        public double Lat { get; private set; }
        public double Lon { get; private set; }
        public float PrecisionM { get; private set; }
        public string Estado { get; private set; } = "GPS apagado";

        public event Action<Estacion> AlEntrarEstacion;
        Estacion ultima;

        public bool HayCoordenadas
        {
            get
            {
                if (kb == null || kb.Datos == null || kb.Datos.estaciones == null) return false;
                foreach (var e in kb.Datos.estaciones) if (Valida(e)) return true;
                return false;
            }
        }

        public static bool Valida(Estacion e) => e != null && (Math.Abs(e.lat) > 0.0001 || Math.Abs(e.lon) > 0.0001);

        IEnumerator Start()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
            {
                Permission.RequestUserPermission(Permission.FineLocation);
                float t = 0;
                while (!Permission.HasUserAuthorizedPermission(Permission.FineLocation) && t < 30f) { t += 0.5f; yield return new WaitForSeconds(0.5f); }
            }
#endif
            // Input.location no tiene equivalente en el Input System nuevo; se protege por si la plataforma lo bloquea.
            bool habilitado;
            try
            {
                habilitado = Input.location.isEnabledByUser;
                if (habilitado) Input.location.Start(precisionDeseadaM, distanciaActualizacionM);
            }
            catch (Exception ex)
            {
                Estado = "GPS no disponible";
                Debug.LogWarning("[GPS] No se pudo iniciar la ubicación: " + ex.Message);
                yield break;
            }
            if (!habilitado) { Estado = "Ubicación desactivada en el celular"; yield break; }

            int espera = 30;
            Estado = "Buscando satélites...";
            while (Input.location.status == LocationServiceStatus.Initializing && espera-- > 0)
                yield return new WaitForSeconds(1f);

            if (Input.location.status != LocationServiceStatus.Running) { Estado = "GPS no disponible"; yield break; }
            Activo = true;

            while (true)
            {
                var d = Input.location.lastData;
                Lat = d.latitude; Lon = d.longitude; PrecisionM = d.horizontalAccuracy;
                Estado = HayCoordenadas ? $"GPS ±{PrecisionM:0} m" : "GPS activo (faltan coordenadas de estaciones)";
                RevisarEstacion();
                yield return new WaitForSeconds(2f);
            }
        }

        void RevisarEstacion()
        {
            var e = Cercana(out float dist);
            if (e == null) return;
            if (dist <= Mathf.Max(e.radio_m, radioMinimoM) && e != ultima)
            {
                ultima = e;
                AlEntrarEstacion?.Invoke(e);
            }
        }

        public Estacion Cercana(out float distanciaM)
        {
            distanciaM = float.MaxValue;
            Estacion mejor = null;
            if (!Activo || kb == null || kb.Datos == null) return null;
            foreach (var e in kb.Datos.estaciones)
            {
                if (!Valida(e)) continue;
                float d = Distancia(Lat, Lon, e.lat, e.lon);
                if (d < distanciaM) { distanciaM = d; mejor = e; }
            }
            return mejor;
        }

        /// <summary>Distancia en metros entre dos coordenadas (formula de Haversine).</summary>
        public static float Distancia(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371000;
            double dLat = (lat2 - lat1) * Math.PI / 180, dLon = (lon2 - lon1) * Math.PI / 180;
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                       Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return (float)(R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a)));
        }

        void OnDestroy()
        {
            if (Activo) Input.location.Stop();
        }
    }
}
