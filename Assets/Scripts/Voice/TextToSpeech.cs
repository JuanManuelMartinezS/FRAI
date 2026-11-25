using System.Runtime.InteropServices;
using UnityEngine;

namespace GuiaFrailejon
{
    /// <summary>
    /// Voz del frailejon usando el motor de voz DEL PROPIO CELULAR (offline si el paquete de voz en espanol
    /// esta descargado). La app debe pedir al usuario descargarlo mientras tenga senal:
    ///   Android: Ajustes > Sistema > Idioma > Salida de texto a voz > Google > Instalar datos de voz > Espanol
    ///   iPhone:  Ajustes > Accesibilidad > Contenido leido > Voces > Espanol
    /// Alternativa 100 % controlada: voces Piper (onnx) empaquetadas en la app.
    /// </summary>
    public interface ITextToSpeech
    {
        void Hablar(string texto);
        void Detener();
        bool Hablando { get; }
    }

    public static class VozFactory
    {
        static ITextToSpeech instancia;
        public static ITextToSpeech Instancia
        {
            get
            {
                if (instancia != null) return instancia;
#if UNITY_ANDROID && !UNITY_EDITOR
                instancia = new VozAndroid();
#elif UNITY_IOS && !UNITY_EDITOR
                instancia = new VozIOS();
#else
                instancia = new VozEditor();
#endif
                return instancia;
            }
        }
    }

    class VozEditor : ITextToSpeech
    {
        float hasta;
        public void Hablar(string t) { Debug.Log("[Voz] " + t); hasta = Time.time + Mathf.Clamp(t.Length * 0.06f, 1f, 15f); }
        public void Detener() => hasta = 0;
        public bool Hablando => Time.time < hasta;
    }

#if UNITY_ANDROID
    class VozAndroid : ITextToSpeech
    {
        AndroidJavaObject tts;
        bool listo;

        class Init : AndroidJavaProxy
        {
            readonly VozAndroid voz;
            public Init(VozAndroid v) : base("android.speech.tts.TextToSpeech$OnInitListener") { voz = v; }
            void onInit(int status) { if (status == 0) voz.Configurar(); }
        }

        public VozAndroid()
        {
            using (var up = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            {
                var act = up.GetStatic<AndroidJavaObject>("currentActivity");
                tts = new AndroidJavaObject("android.speech.tts.TextToSpeech", act, new Init(this));
            }
        }

        void Configurar()
        {
            int r = tts.Call<int>("setLanguage", new AndroidJavaObject("java.util.Locale", "es", "CO"));
            if (r < 0) r = tts.Call<int>("setLanguage", new AndroidJavaObject("java.util.Locale", "es"));
            if (r < 0) Debug.LogWarning("[Voz] No hay voz en espanol instalada en este celular.");
            tts.Call<int>("setSpeechRate", 0.95f);
            listo = r >= 0;
        }

        public void Hablar(string t)
        {
            if (!listo || string.IsNullOrEmpty(t)) return;
            tts.Call<int>("speak", t, 0 /*QUEUE_FLUSH*/, null, "frai");
        }
        public void Detener() { if (tts != null) tts.Call<int>("stop"); }
        public bool Hablando => tts != null && listo && tts.Call<bool>("isSpeaking");
    }
#endif

#if UNITY_IOS
    class VozIOS : ITextToSpeech
    {
        [DllImport("__Internal")] static extern void GuiaTTS_Hablar(string texto, string idioma);
        [DllImport("__Internal")] static extern void GuiaTTS_Detener();
        [DllImport("__Internal")] static extern bool GuiaTTS_Hablando();

        public void Hablar(string t) { if (!string.IsNullOrEmpty(t)) GuiaTTS_Hablar(t, "es-CO"); }
        public void Detener() => GuiaTTS_Detener();
        public bool Hablando => GuiaTTS_Hablando();
    }
#endif
}
