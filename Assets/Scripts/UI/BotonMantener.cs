using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace GuiaFrailejon
{
    /// <summary>Boton de "mantener presionado para hablar": avisa al presionar y al soltar.</summary>
    public class BotonMantener : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public UnityEvent alPresionar = new UnityEvent();
        public UnityEvent alSoltar = new UnityEvent();
        bool presionado;

        public void OnPointerDown(PointerEventData e)
        {
            presionado = true;
            alPresionar.Invoke();
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (!presionado) return;
            presionado = false;
            alSoltar.Invoke();
        }

        void OnDisable()
        {
            if (!presionado) return;
            presionado = false;
            alSoltar.Invoke();
        }
    }
}
