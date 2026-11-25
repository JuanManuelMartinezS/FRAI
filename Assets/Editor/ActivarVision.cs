#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GuiaFrailejon.EditorTools
{
    /// <summary>
    /// Menu  Guia Frailejon > Activar vision artificial
    /// Paso 1: instala el paquete Unity Inference Engine (com.unity.ai.inference).
    /// Paso 2: agrega el simbolo GUIA_VISION (Unity recompila).
    /// Paso 3: conecta el modelo .onnx y vision.json al ClasificadorEspecies de la escena.
    ///
    /// Para usar el modelo REAL: reemplacen Assets/Vision/modelo_especies.onnx y Assets/Vision/vision.json
    /// por los que genera packages/ml-vision-pipeline (preferir el ONNX FP32 o FP16; el INT8 QDQ esta pensado para WASM).
    /// </summary>
    public static class ActivarVision
    {
        const string Simbolo = "GUIA_VISION";
        const string Paquete = "com.unity.ai.inference";
        public const string Carpeta = "Assets/Vision";

        static AddRequest instalacion;

        [MenuItem("Guia Frailejon/Activar visión artificial", priority = 22)]
        public static void Activar()
        {
            // ---- Paso 1: paquete ----
            if (ActivarIA.BuscarTipo("Unity.InferenceEngine.ModelAsset") == null)
            {
                if (instalacion != null && !instalacion.IsCompleted)
                {
                    EditorUtility.DisplayDialog("Activar visión", "El paquete se está instalando. Esperen a que termine.", "OK");
                    return;
                }
                if (!EditorUtility.DisplayDialog("Activar visión — paso 1 de 3",
                    "Se va a instalar el paquete Unity Inference Engine (" + Paquete + "), que ejecuta el modelo de visión en el celular.\n\n" +
                    "Cuando termine de instalar y recompilar, ejecuten OTRA VEZ este menú.", "Instalar", "Cancelar"))
                    return;
                instalacion = Client.Add(Paquete);
                EditorApplication.update += VigilarInstalacion;
                return;
            }

            // ---- Paso 2: simbolo ----
            if (!TieneSimbolo())
            {
                AgregarSimbolo(NamedBuildTarget.Android);
                AgregarSimbolo(NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup));
                EditorUtility.DisplayDialog("Activar visión — paso 2 de 3",
                    "Listo. Unity va a recompilar los scripts.\n\nCuando termine, ejecuten OTRA VEZ:\nGuia Frailejon > Activar visión artificial", "OK");
                return;
            }

            // ---- Paso 3: conectar ----
            var cls = Object.FindAnyObjectByType<ClasificadorEspecies>(FindObjectsInactive.Include);
            if (cls == null)
            {
                EditorUtility.DisplayDialog("Falta la escena",
                    "Abran Assets/Scenes/GuiaFrailejon.unity (o ejecuten 'Armar escena completa') y vuelvan a intentar.", "OK");
                return;
            }
            string msg = Conectar(cls);
            EditorSceneManager.MarkSceneDirty(cls.gameObject.scene);
            EditorSceneManager.SaveScene(cls.gameObject.scene);
            Selection.activeObject = cls;
            EditorUtility.DisplayDialog("Activar visión — paso 3 de 3", msg, "OK");
        }

        static void VigilarInstalacion()
        {
            if (instalacion == null || !instalacion.IsCompleted) return;
            EditorApplication.update -= VigilarInstalacion;
            if (instalacion.Status == StatusCode.Success)
                EditorUtility.DisplayDialog("Activar visión", "Paquete instalado: " + instalacion.Result.displayName + " " + instalacion.Result.version +
                    "\n\nCuando Unity termine de recompilar, ejecuten OTRA VEZ:\nGuia Frailejon > Activar visión artificial", "OK");
            else
                EditorUtility.DisplayDialog("Activar visión", "No se pudo instalar el paquete:\n" + instalacion.Error?.message +
                    "\n\nInstálenlo a mano: Window > Package Manager > + > Install package by name > " + Paquete, "OK");
            instalacion = null;
        }

        public static bool TieneSimbolo() =>
            PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Android).Split(';').Contains(Simbolo);

        static void AgregarSimbolo(NamedBuildTarget t)
        {
            string s = PlayerSettings.GetScriptingDefineSymbols(t);
            if (s.Split(';').Contains(Simbolo)) return;
            PlayerSettings.SetScriptingDefineSymbols(t, string.IsNullOrEmpty(s) ? Simbolo : s + ";" + Simbolo);
        }

        /// <summary>Asigna modelo, metadatos y fichas. Devuelve un resumen para mostrar.</summary>
        public static string Conectar(ClasificadorEspecies cls)
        {
            string onnx = Directory.Exists(Carpeta)
                ? Directory.GetFiles(Carpeta, "*.onnx").Select(p => p.Replace('\\', '/')).OrderBy(p => p).FirstOrDefault()
                : null;
            if (onnx != null)
            {
                AssetDatabase.ImportAsset(onnx, ImportAssetOptions.ForceUpdate);
                cls.modelo = AssetDatabase.LoadMainAssetAtPath(onnx);
            }
            cls.metadatos = AssetDatabase.LoadAssetAtPath<TextAsset>(Carpeta + "/vision.json");
            cls.fichas = AssetDatabase.LoadAssetAtPath<TextAsset>(Carpeta + "/especies_info.json");
            EditorUtility.SetDirty(cls);

            bool demo = cls.metadatos != null && cls.metadatos.text.Contains("\"demo\": true");
            if (onnx == null) return "No hay ningún .onnx en " + Carpeta + ". Copien ahí el modelo y vuelvan a ejecutar el menú.";
            return "Visión conectada.\n\nModelo: " + onnx + "\nEspecies: " + (cls.metadatos ? "vision.json" : "FALTA vision.json") +
                   (demo ? "\n\nAVISO: es el modelo de DEMOSTRACIÓN (pesos aleatorios). Reemplácenlo por el entrenado: " +
                           "copien el .onnx y el vision.json del pipeline en " + Carpeta + " y ejecuten este menú otra vez." : "") +
                   "\n\nPrueben con Play: pestaña «Explorar».";
        }
    }
}
#endif
