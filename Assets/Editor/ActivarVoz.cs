#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GuiaFrailejon.EditorTools
{
    /// <summary>
    /// Menu  Guia Frailejon > Activar voz (hablarle a Frai)
    /// Paso 1: revisa el paquete whisper.unity y el modelo, y agrega el simbolo GUIA_WHISPER (Unity recompila).
    /// Paso 2: agrega WhisperManager + WhisperSTT al objeto Sistema y los conecta con la interfaz.
    /// </summary>
    public static class ActivarVoz
    {
        const string Simbolo = "GUIA_WHISPER";
        const string CarpetaModelos = "Assets/StreamingAssets/Whisper";
        const string PromptInicial =
            "Frailejón, páramo, Aguacerales, Arenales, Valle Lunar, Valle de las Tumbas, Nevado del Ruiz, Kumanday, " +
            "cóndor, mal de altura, guardaparque, ¿qué es esto?";

        [MenuItem("Guia Frailejon/Activar voz (hablarle a Frai)", priority = 21)]
        public static void Activar()
        {
            var tWhisper = ActivarIA.BuscarTipo("Whisper.WhisperManager");
            if (tWhisper == null)
            {
                EditorUtility.DisplayDialog("Falta whisper.unity",
                    "Primero instalen el paquete:\n\nWindow > Package Manager > botón + > Install package from git URL\n\n" +
                    "https://github.com/Macoron/whisper.unity.git?path=/Packages/com.whisper.unity\n\n" +
                    "Cuando termine, ejecuten este menú otra vez.", "OK");
                return;
            }

            string modelo = BuscarModelo();
            if (modelo == null)
            {
                Directory.CreateDirectory(CarpetaModelos);
                AssetDatabase.Refresh();
                EditorUtility.DisplayDialog("Falta el modelo de voz",
                    "Descarguen el modelo de Whisper y pónganlo en la carpeta:\n\n" + CarpetaModelos + "\n\n" +
                    "Recomendado para español: ggml-base.bin (unos 140 MB).\n" +
                    "Más liviano pero entiende peor: ggml-tiny.bin (unos 75 MB).\n\n" +
                    "Se descargan de: huggingface.co/ggerganov/whisper.cpp (pestaña Files).\n\n" +
                    "Cuando el archivo esté en la carpeta, ejecuten este menú otra vez.", "OK");
                return;
            }

            // ---- Paso 1: simbolo de compilacion ----
            var objetivo = NamedBuildTarget.Android;
            string simbolos = PlayerSettings.GetScriptingDefineSymbols(objetivo);
            if (!simbolos.Split(';').Contains(Simbolo))
            {
                PlayerSettings.SetScriptingDefineSymbols(objetivo, string.IsNullOrEmpty(simbolos) ? Simbolo : simbolos + ";" + Simbolo);
                EditorUtility.DisplayDialog("Activar voz — paso 1 de 2",
                    "Listo. Unity va a recompilar (esperen a que desaparezca el circulito de abajo a la derecha).\n\n" +
                    "Cuando termine, ejecuten OTRA VEZ:\nGuia Frailejon > Activar voz", "OK");
                return;
            }

            // ---- Paso 2: componentes ----
            var sistema = GameObject.Find("Sistema");
            var ui = Object.FindFirstObjectByType<GuideUI>(FindObjectsInactive.Include);
            if (sistema == null || ui == null)
            {
                EditorUtility.DisplayDialog("Falta la escena",
                    "Abran la escena Assets/Scenes/GuiaFrailejon.unity (o ejecuten 'Armar escena completa') y vuelvan a intentar.", "OK");
                return;
            }

            Component whisper = sistema.GetComponent(tWhisper);
            if (whisper == null) whisper = sistema.AddComponent(tWhisper);

            // Ruta del modelo relativa a StreamingAssets, p. ej. "Whisper/ggml-base.bin"
            string relativa = modelo.Substring("Assets/StreamingAssets/".Length);
            ActivarIA.AsignarMiembro(whisper, "IsModelPathInStreamingAssets", true);
            ActivarIA.AsignarMiembro(whisper, "ModelPath", relativa);
            ActivarIA.AsignarMiembro(whisper, "language", "es");
            ActivarIA.AsignarMiembro(whisper, "translateToEnglish", false);
            ActivarIA.AsignarMiembro(whisper, "initialPrompt", PromptInicial);

            var stt = sistema.GetComponent<WhisperSTT>();
            if (stt == null) stt = sistema.AddComponent<WhisperSTT>();
            if (!ActivarIA.AsignarMiembro(stt, "whisper", whisper))
                Debug.LogWarning("[ActivarVoz] Arrastren el componente WhisperManager al campo 'Whisper' de WhisperSTT a mano.");

            ui.sttComponente = stt;

            EditorUtility.SetDirty(whisper);
            EditorUtility.SetDirty(stt);
            EditorUtility.SetDirty(ui);
            EditorSceneManager.MarkSceneDirty(sistema.scene);
            EditorSceneManager.SaveScene(sistema.scene);
            Selection.activeGameObject = sistema;

            EditorUtility.DisplayDialog("Activar voz — listo",
                "La voz quedó conectada con el modelo:\n" + relativa + "\n\n" +
                "En la app aparece el botón amarillo 'Mantén para hablar'.\n" +
                "La primera vez el celular pedirá permiso de micrófono.", "OK");
        }

        static string BuscarModelo()
        {
            string[] preferidos = { "ggml-base.bin", "ggml-base-q5_1.bin", "ggml-small.bin", "ggml-tiny.bin" };
            foreach (var nombre in preferidos)
            {
                string ruta = CarpetaModelos + "/" + nombre;
                if (File.Exists(ruta)) return ruta;
            }
            if (Directory.Exists("Assets/StreamingAssets"))
            {
                var cualquiera = Directory.GetFiles("Assets/StreamingAssets", "ggml-*.bin", SearchOption.AllDirectories).FirstOrDefault();
                if (cualquiera != null) return cualquiera.Replace('\\', '/');
            }
            return null;
        }
    }
}
#endif
