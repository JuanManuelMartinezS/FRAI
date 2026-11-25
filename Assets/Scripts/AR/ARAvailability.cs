using System.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace GuiaFrailejon
{
    /// <summary>
    /// Si el celular no soporta ARCore/ARKit, cambia al "modo sin AR":
    /// el frailejon aparece en pantalla normal con una camara 3D comun y el visitante elige la estacion.
    /// </summary>
    public class ARAvailability : MonoBehaviour
    {
        public GameObject raizAR;        // XR Origin + AR Session
        public GameObject raizSinAR;     // Camara normal + frailejon fijo + selector de estacion
        public bool ModoAR { get; private set; }

        IEnumerator Start()
        {
            raizSinAR.SetActive(false);
            if (ARSession.state == ARSessionState.None || ARSession.state == ARSessionState.CheckingAvailability)
                yield return ARSession.CheckAvailability();

            ModoAR = ARSession.state != ARSessionState.Unsupported;
            raizAR.SetActive(ModoAR);
            raizSinAR.SetActive(!ModoAR);
            Debug.Log("[AR] Modo AR: " + ModoAR);
        }

        /// <summary>Boton "ahorrar bateria": apaga la camara y deja solo la conversacion.</summary>
        public void AlternarCamara(bool encendida)
        {
            if (!ModoAR) return;
            raizAR.SetActive(encendida);
            raizSinAR.SetActive(!encendida);
        }
    }
}
