using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

namespace GuiaFrailejon
{
    /// <summary>Contrato de voz a texto (para poder cambiar de motor).</summary>
    public interface ISpeechToText
    {
        bool Disponible { get; }
        bool Cargando { get; }
        bool Grabando { get; }
        void Empezar();
        Task<string> Terminar();
    }

    /// <summary>
    /// Voz a texto 100 % offline con Whisper (paquete whisper.unity).
    /// Se activa con el menu  Guia Frailejon > Activar voz  (agrega GUIA_WHISPER y lo conecta).
    /// Graba con el microfono mientras el visitante mantiene presionado el boton, y al soltar transcribe.
    /// </summary>
    public class WhisperSTT : MonoBehaviour, ISpeechToText
    {
#if GUIA_WHISPER
        public Whisper.WhisperManager whisper;
#endif
        [Tooltip("Duracion maxima de una pregunta hablada.")]
        public int maxSegundos = 15;
        const int Frecuencia = 16000;

        AudioClip clip;
        public bool Grabando { get; private set; }

        public bool Disponible
        {
            get
            {
#if GUIA_WHISPER
                return whisper != null && whisper.IsLoaded;
#else
                return false;
#endif
            }
        }

        public bool Cargando
        {
            get
            {
#if GUIA_WHISPER
                return whisper != null && whisper.IsLoading;
#else
                return false;
#endif
            }
        }

        public void Empezar()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
            {
                Permission.RequestUserPermission(Permission.Microphone);
                return;
            }
#endif
            if (Grabando || Microphone.devices.Length == 0) return;
            clip = Microphone.Start(null, false, maxSegundos, Frecuencia);
            Grabando = clip != null;
        }

        public async Task<string> Terminar()
        {
            if (!Grabando) return "";
            int pos = Microphone.GetPosition(null);
            Microphone.End(null);
            Grabando = false;
            if (clip == null || pos < Frecuencia * 0.4f) return "";   // menos de 0,4 s: lo ignoramos

            var muestras = new float[pos * clip.channels];
            clip.GetData(muestras, 0);
#if GUIA_WHISPER
            var r = await whisper.GetTextAsync(muestras, clip.frequency, clip.channels);
            return LimpiarTexto(r != null ? r.Result : "");
#else
            await Task.Yield();
            return "";
#endif
        }

        /// <summary>Whisper a veces escribe cosas como [Musica] o (ruido) cuando hay silencio o viento.</summary>
        static string LimpiarTexto(string t)
        {
            if (string.IsNullOrWhiteSpace(t)) return "";
            t = Regex.Replace(t, @"\[[^\]]*\]|\([^\)]*\)|\*[^\*]*\*", " ");
            t = Regex.Replace(t, @"\s+", " ").Trim();
            return t.Length < 2 ? "" : t;
        }
    }
}
