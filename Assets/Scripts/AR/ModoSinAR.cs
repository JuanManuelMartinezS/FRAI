using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GuiaFrailejon
{
    /// <summary>
    /// Modo para celulares sin realidad aumentada: Frai en 3D delante de una foto de la estacion.
    /// Al inclinar el celular la camara se mueve un poco (efecto de profundidad).
    /// Va en RaizSinAR.
    /// </summary>
    public class ModoSinAR : MonoBehaviour
    {
        public KnowledgeBase kb;
        public Camera camara;
        public Renderer fondo;
        [Tooltip("Una foto por estacion, en el mismo orden de base_conocimiento.json.")]
        public Texture2D[] fotos;
        [Tooltip("Grados que gira la camara al inclinar el celular.")]
        public float inclinacionMax = 4f;

        Quaternion rotBase;
        Material mat;

        void Start()
        {
            if (camara) rotBase = camara.transform.localRotation;
            if (fondo) mat = fondo.material;   // copia propia del material
            if (fotos != null && fotos.Length > 0 && fotos[0] && mat) mat.mainTexture = fotos[0];
        }

        public void MostrarEstacion(string estacionId)
        {
            if (mat == null || fotos == null || kb == null || kb.Datos == null) return;
            int i = Array.FindIndex(kb.Datos.estaciones, e => e.id == estacionId);
            if (i >= 0 && i < fotos.Length && fotos[i] != null) mat.mainTexture = fotos[i];
        }

        void Update()
        {
            if (!camara) return;
            // Con el celular vertical, la gravedad es aprox (0, -1, 0). Inclinar a los lados cambia x; hacia adelante/atras cambia z.
            Vector3 a = Inclinacion();
            float giroY = Mathf.Clamp(a.x, -1f, 1f) * inclinacionMax;
            float giroX = Mathf.Clamp(a.z, -1f, 1f) * inclinacionMax * 0.5f;
            var objetivo = rotBase * Quaternion.Euler(giroX, giroY, 0);
            camara.transform.localRotation = Quaternion.Slerp(camara.transform.localRotation, objetivo, 4f * Time.deltaTime);
        }

        /// <summary>Direccion de la gravedad (largo 1) con el Input System nuevo. Cero si no hay acelerometro.</summary>
        static Vector3 Inclinacion()
        {
            var acel = Accelerometer.current;
            if (acel == null) return Vector3.zero;
            if (!acel.enabled) InputSystem.EnableDevice(acel);
            Vector3 v = acel.acceleration.ReadValue();
            // Se normaliza para no depender de si el sensor reporta en g o en m/s².
            return v.sqrMagnitude > 1e-4f ? v.normalized : Vector3.zero;
        }
    }
}
