using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GuiaFrailejon
{
    /// <summary>Boton que alterna entre el frailejon y el condor. Va en el boton flotante "Cambiar".</summary>
    [RequireComponent(typeof(Button))]
    public class SelectorAvatar : MonoBehaviour
    {
        public TMP_Text etiqueta;

        void Awake() => GetComponent<Button>().onClick.AddListener(AvatarSeleccion.Alternar);
        void OnEnable() { AvatarSeleccion.Cambio += Refrescar; Refrescar(); }
        void OnDisable() => AvatarSeleccion.Cambio -= Refrescar;

        // La etiqueta dice a quien se cambia al tocar.
        void Refrescar()
        {
            if (etiqueta) etiqueta.text = AvatarSeleccion.Actual == Avatar.Frailejon ? "Ser Cóndor" : "Ser Frailejón";
        }
    }
}
