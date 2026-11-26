using UnityEngine;

namespace GuiaFrailejon
{
    /// <summary>
    /// Sube este RectTransform (anclado abajo) por encima del teclado del movil para que
    /// el campo de texto no quede tapado mientras se escribe.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class SubirConTeclado : MonoBehaviour
    {
        [Tooltip("Margen extra entre el teclado y el panel (unidades de canvas).")]
        public float margen = 12;
        [Tooltip("Velocidad de la animacion (unidades de canvas por segundo).")]
        public float velocidad = 4000;

        RectTransform rt;
        Canvas canvas;
        float yBase;
        float desplazamiento;

        void Awake()
        {
            rt = (RectTransform)transform;
            yBase = rt.anchoredPosition.y;
        }

        void LateUpdate()
        {
            if (!canvas) canvas = GetComponentInParent<Canvas>()?.rootCanvas;
            float escala = canvas && canvas.scaleFactor > 0 ? canvas.scaleFactor : 1f;

            float objetivo = 0;
            if (TouchScreenKeyboard.visible)
            {
                // La zona segura ya deja libre la barra de gestos: no contarla dos veces.
                float tecladoPx = TouchScreenKeyboard.area.height - Screen.safeArea.yMin;
                if (tecladoPx > 0) objetivo = tecladoPx / escala + margen - yBase;
                objetivo = Mathf.Max(0, objetivo);
            }

            desplazamiento = Mathf.MoveTowards(desplazamiento, objetivo, velocidad * Time.unscaledDeltaTime);
            var p = rt.anchoredPosition;
            p.y = yBase + desplazamiento;
            rt.anchoredPosition = p;
        }

        void OnDisable()
        {
            desplazamiento = 0;
            if (rt) { var p = rt.anchoredPosition; p.y = yBase; rt.anchoredPosition = p; }
        }
    }
}
