using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace GuiaFrailejon
{
    /// <summary>
    /// Modo acompanante: Frai camina al lado del visitante.
    ///
    /// Como funciona:
    /// - Frai se ubica segun la POSICION del visitante y la DIRECCION EN LA QUE CAMINA, no hacia donde apunta el celular.
    ///   Si el visitante gira sin caminar, Frai se queda quieto en su sitio (puede quedar fuera de la pantalla).
    /// - Cuando el visitante avanza mas de 'pasoMinimo' metros, Frai recalcula su lugar: a la derecha del camino.
    /// - Antes de ubicarse revisa lo que ARCore ve: busca piso y evita quedar detras de una pared o mueble
    ///   (planos verticales). Si la derecha esta bloqueada, prueba la izquierda, y luego un poco atras.
    /// - Si Frai queda fuera de la pantalla, un indicador en el borde muestra hacia donde esta.
    /// - "Ven aqui" lo trae al frente del visitante.
    /// Va en el XR Origin.
    /// </summary>
    [RequireComponent(typeof(ARRaycastManager))]
    public class FraiAcompanante : MonoBehaviour
    {
        public GameObject prefabFrailejon;
        [Tooltip("Altura de Frai en modo acompanante (el prefab mide 1 m).")]
        public float escala = 0.8f;
        [Tooltip("Metros a un lado del camino del visitante.")]
        public float lado = 0.8f;
        [Tooltip("Metros hacia adelante (en la direccion en que camina el visitante).")]
        public float adelante = 0.3f;
        [Tooltip("Cuanto debe caminar el visitante para que Frai cambie de lugar.")]
        public float pasoMinimo = 0.6f;
        public float velocidad = 1.3f;
        [Tooltip("Si el visitante se aleja mas que esto, Frai reaparece a su lado.")]
        public float distanciaTeletransporte = 7f;
        [Tooltip("Altura aproximada del celular sobre el piso, si todavia no se detecta el piso.")]
        public float alturaCelular = 1.35f;

        [Header("Indicador en el borde de la pantalla (opcional)")]
        public RectTransform indicador;
        public TMP_Text textoIndicador;

        public bool Activo { get; private set; }

        static readonly List<ARRaycastHit> hits = new List<ARRaycastHit>();
        ARRaycastManager rc;
        ARPlaneManager planos;
        Transform frai;
        ITextToSpeech voz;

        Vector3 anclaUsuario;   // donde estaba el visitante la ultima vez que Frai se reubico
        Vector3 rumbo;          // direccion en que camina el visitante (horizontal)
        Vector3 objetivo;       // lugar donde debe pararse Frai
        float sueloY;
        bool haySuelo;

        void Awake()
        {
            rc = GetComponent<ARRaycastManager>();
            planos = GetComponent<ARPlaneManager>();
        }

        void Start()
        {
            voz = VozFactory.Instancia;
            if (indicador) indicador.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------
        // API
        // ------------------------------------------------------------------
        public void Activar(Vector3? desde)
        {
            var cam = Camera.main;
            if (cam == null || prefabFrailejon == null) return;
            if (frai == null)
            {
                frai = Instantiate(prefabFrailejon).transform;
                frai.name = "FraiAcompanante";
                frai.localScale = Vector3.one * escala;
            }
            frai.gameObject.SetActive(true);
            haySuelo = false;
            sueloY = cam.transform.position.y - alturaCelular;

            anclaUsuario = Plano(cam.transform.position);
            rumbo = Plano(cam.transform.forward);
            if (rumbo.sqrMagnitude < 0.01f) rumbo = Vector3.forward;
            rumbo.Normalize();
            objetivo = CalcularObjetivo(anclaUsuario, rumbo, cam);

            Vector3 inicio = desde ?? objetivo;
            inicio.y = Suelo(inicio);
            frai.position = inicio;
            Activo = true;
        }

        public void Desactivar()
        {
            Activo = false;
            if (frai) frai.gameObject.SetActive(false);
            if (indicador) indicador.gameObject.SetActive(false);
        }

        /// <summary>Frai aparece en un punto (p. ej. el letrero de la nueva estacion) y desde ahi vuelve al lado del visitante.</summary>
        public void Llamar(Vector3 punto)
        {
            if (!Activo || frai == null) return;
            punto.y = Suelo(punto);
            frai.position = punto;
        }

        /// <summary>Trae a Frai al frente del visitante, a la vista. Se queda ahi hasta que el visitante camine.</summary>
        public void VenAqui()
        {
            var cam = Camera.main;
            if (!Activo || cam == null) return;
            Vector3 u = Plano(cam.transform.position);
            Vector3 f = Plano(cam.transform.forward);
            if (f.sqrMagnitude < 0.01f) f = rumbo;
            f.Normalize();
            anclaUsuario = u;
            objetivo = u + f * 1.3f;
            objetivo.y = Suelo(objetivo);
        }

        // ------------------------------------------------------------------
        // Logica
        // ------------------------------------------------------------------
        void Update()
        {
            if (!Activo || frai == null) return;
            var cam = Camera.main;
            if (cam == null) return;

            // 1) Solo se reubica cuando el visitante CAMINA (no cuando gira el celular).
            Vector3 usuario = Plano(cam.transform.position);
            Vector3 mov = usuario - anclaUsuario;
            if (mov.magnitude > pasoMinimo)
            {
                rumbo = Vector3.Slerp(rumbo, mov.normalized, 0.6f).normalized;
                anclaUsuario = usuario;
                objetivo = CalcularObjetivo(usuario, rumbo, cam);
            }

            // 2) Si el visitante se fue lejos, Frai reaparece a su lado.
            Vector3 delta = objetivo - frai.position; delta.y = 0;
            float d = delta.magnitude;
            if (Plano(frai.position - cam.transform.position).magnitude > distanciaTeletransporte)
            {
                frai.position = objetivo;
                return;
            }

            // 3) Caminar hacia su lugar dando saltitos.
            bool moviendo = d > 0.2f;
            Vector3 pos = frai.position;
            Vector3 nueva = pos;
            if (moviendo)
            {
                float v = velocidad * (d > 1.5f ? 2f : 1f);
                nueva += delta.normalized * Mathf.Min(d, v * Time.deltaTime);
            }
            float salto = moviendo ? Mathf.Abs(Mathf.Sin(Time.time * 9f)) * 0.07f : 0f;
            nueva.y = Mathf.Lerp(pos.y, objetivo.y + salto, 12f * Time.deltaTime);
            frai.position = nueva;

            // 4) Mira hacia donde camina; quieto o hablando, mira al visitante.
            bool hablando = voz != null && voz.Hablando;
            Vector3 mirar = (moviendo && !hablando) ? delta : cam.transform.position - nueva;
            mirar.y = 0;
            if (mirar.sqrMagnitude > 0.001f)
                frai.rotation = Quaternion.Slerp(frai.rotation, Quaternion.LookRotation(mirar), 6f * Time.deltaTime);
        }

        void LateUpdate() => ActualizarIndicador();

        /// <summary>Busca un lugar libre al lado del visitante: derecha, izquierda, derecha-atras, izquierda-atras.</summary>
        Vector3 CalcularObjetivo(Vector3 usuario, Vector3 dir, Camera cam)
        {
            Vector3 derecha = Vector3.Cross(Vector3.up, dir).normalized;
            Vector3[] candidatos =
            {
                usuario + derecha * lado + dir * adelante,
                usuario - derecha * lado + dir * adelante,
                usuario + derecha * lado - dir * 0.6f,
                usuario - derecha * lado - dir * 0.6f,
            };

            Vector3 ojos = cam.transform.position;
            Vector3? sinPisoPeroLibre = null;
            foreach (var c in candidatos)
            {
                if (Bloqueado(ojos, c)) continue;
                if (TienePiso(c, out float y)) { var p = c; p.y = y; sueloY = y; haySuelo = true; return p; }
                if (sinPisoPeroLibre == null) sinPisoPeroLibre = c;
            }
            var res = sinPisoPeroLibre ?? candidatos[0];
            res.y = Suelo(res);
            return res;
        }

        /// <summary>¿Hay una pared o mueble (plano vertical detectado) entre el visitante y el punto?</summary>
        bool Bloqueado(Vector3 ojos, Vector3 punto)
        {
            if (rc == null) return false;
            Vector3 destino = punto; destino.y = ojos.y - 0.5f;   // a la altura del cuerpo de Frai
            Vector3 dir = destino - ojos;
            float dist = dir.magnitude;
            if (dist < 0.05f) return false;
            if (!rc.Raycast(new Ray(ojos, dir / dist), hits, TrackableType.PlaneWithinPolygon)) return false;
            foreach (var h in hits)
            {
                if (h.distance > dist + 0.2f) break;
                var plano = planos != null ? planos.GetPlane(h.trackableId) : null;
                if (plano != null && plano.alignment == PlaneAlignment.Vertical) return true;
            }
            return false;
        }

        bool TienePiso(Vector3 p, out float y)
        {
            y = 0;
            if (rc == null) return false;
            if (!rc.Raycast(new Ray(p + Vector3.up * 1.5f, Vector3.down), hits, TrackableType.PlaneWithinPolygon)) return false;
            foreach (var h in hits)
            {
                var plano = planos != null ? planos.GetPlane(h.trackableId) : null;
                if (plano == null || plano.alignment == PlaneAlignment.HorizontalUp) { y = h.pose.position.y; return true; }
            }
            return false;
        }

        float Suelo(Vector3 p)
        {
            if (TienePiso(p, out float y)) { haySuelo = true; sueloY = y; }
            else if (!haySuelo && Camera.main) sueloY = Camera.main.transform.position.y - alturaCelular;
            return sueloY;
        }

        void ActualizarIndicador()
        {
            if (indicador == null) return;
            var cam = Camera.main;
            if (!Activo || frai == null || cam == null) { indicador.gameObject.SetActive(false); return; }

            Vector3 vp = cam.WorldToViewportPoint(frai.position + Vector3.up * escala * 0.5f);
            bool visible = vp.z > 0 && vp.x > 0.03f && vp.x < 0.97f && vp.y > 0.03f && vp.y < 0.97f;
            indicador.gameObject.SetActive(!visible);
            if (visible) return;

            // Detras de la camara: se invierte para que el indicador apunte al lado correcto.
            if (vp.z < 0) { vp.x = 1f - vp.x; vp.y = 1f - vp.y; }
            bool izquierda = vp.x < 0.5f;
            float x = izquierda ? 0.1f : 0.9f;
            float y = Mathf.Clamp(vp.y, 0.45f, 0.6f);   // franja libre entre la barra de arriba y el panel de abajo
            indicador.anchorMin = indicador.anchorMax = new Vector2(x, y);
            indicador.anchoredPosition = Vector2.zero;
            if (textoIndicador) textoIndicador.text = izquierda ? "<  Frai" : "Frai  >";
        }

        static Vector3 Plano(Vector3 v) { v.y = 0; return v; }
    }
}
