using UnityEngine;

namespace GuiaFrailejon
{
    /// <summary>
    /// Anima al frailejon mientras habla. Si el modelo GLB trae un Animator con el parametro bool "Hablando",
    /// lo usa; si no, hace un balanceo procedural suave (sirve para el prototipo sin animaciones).
    /// Va en la raiz del prefab del frailejon.
    /// </summary>
    public class FrailejonAnimator : MonoBehaviour
    {
        public Animator animator;
        public Transform roseta;              // la parte de las hojas (opcional)
        public float amplitud = 4f, velocidad = 6f;

        static readonly int Hablando = Animator.StringToHash("Hablando");
        ITextToSpeech tts;
        Quaternion rotBase;

        void Start()
        {
            tts = VozFactory.Instancia;
            if (roseta == null) roseta = transform;
            rotBase = roseta.localRotation;
        }

        void Update()
        {
            bool habla = tts != null && tts.Hablando;
            if (animator != null) { animator.SetBool(Hablando, habla); return; }

            float t = habla ? Mathf.Sin(Time.time * velocidad) * amplitud : Mathf.Sin(Time.time * 1.2f) * 1f;
            roseta.localRotation = rotBase * Quaternion.Euler(t * 0.5f, 0, t);
        }
    }
}
