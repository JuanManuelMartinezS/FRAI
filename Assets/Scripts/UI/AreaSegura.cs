using UnityEngine;

namespace GuiaFrailejon
{
    /// <summary>
    /// Ajusta este RectTransform a la zona segura de la pantalla (sin notch, barra de estado ni barra de gestos).
    /// Todo lo interactivo va dentro; los fondos que deben llegar al borde van afuera.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class AreaSegura : MonoBehaviour
    {
        Rect ultima;
        Vector2Int pantalla;

        void Update()
        {
            var zona = Screen.safeArea;
            if (zona == ultima && pantalla.x == Screen.width && pantalla.y == Screen.height) return;
            ultima = zona;
            pantalla = new Vector2Int(Screen.width, Screen.height);
            if (Screen.width <= 0 || Screen.height <= 0) return;

            var rt = (RectTransform)transform;
            rt.anchorMin = new Vector2(zona.xMin / Screen.width, zona.yMin / Screen.height);
            rt.anchorMax = new Vector2(zona.xMax / Screen.width, zona.yMax / Screen.height);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }
}
