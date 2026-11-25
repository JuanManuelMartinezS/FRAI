#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GuiaFrailejon.EditorTools
{
    /// <summary>
    /// Menu  Guia Frailejon > Activar IA (nivel completo)
    /// Paso 1 (primera vez): agrega el simbolo GUIA_LLMUNITY y Unity recompila.
    /// Paso 2 (segunda vez): agrega LLM + LLMAgent + adaptador al objeto Sistema y los conecta.
    /// Usa reflexion para no depender del paquete al compilar.
    /// </summary>
    public static class ActivarIA
    {
        const string Simbolo = "GUIA_LLMUNITY";
        const string PromptSistema =
            "Eres Frai, un frailejon que guia visitantes del Parque Nacional Natural Los Nevados. " +
            "Respondes en espanol, breve y amable, usando solo la informacion oficial que se te entrega.";

        [MenuItem("Guia Frailejon/Activar IA (nivel completo)", priority = 20)]
        public static void Activar()
        {
            Type tLLM = BuscarTipo("LLMUnity.LLM");
            Type tAgente = BuscarTipo("LLMUnity.LLMAgent");

            if (tLLM == null)
            {
                EditorUtility.DisplayDialog("Falta LLM for Unity",
                    "Primero instalen el paquete:\n\nWindow > Package Manager > botón + > Install package from git URL\n\n" +
                    "https://github.com/undreamai/LLMUnity.git\n\nCuando termine de instalar, ejecuten este menú otra vez.", "OK");
                return;
            }
            if (tAgente == null)
            {
                EditorUtility.DisplayDialog("Versión distinta de LLM for Unity",
                    "Su versión del paquete no tiene el componente LLMAgent (puede llamarse LLMCharacter en versiones viejas).\n\n" +
                    "Actualicen el paquete a la versión más reciente desde el Package Manager.", "OK");
                return;
            }

            // ---- Paso 1: simbolo de compilacion ----
            var objetivo = NamedBuildTarget.Android;
            string simbolos = PlayerSettings.GetScriptingDefineSymbols(objetivo);
            if (!simbolos.Split(';').Contains(Simbolo))
            {
                PlayerSettings.SetScriptingDefineSymbols(objetivo, string.IsNullOrEmpty(simbolos) ? Simbolo : simbolos + ";" + Simbolo);
                EditorUtility.DisplayDialog("Activar IA — paso 1 de 2",
                    "Listo. Unity va a recompilar los scripts (esperen a que desaparezca el circulito de abajo a la derecha).\n\n" +
                    "Cuando termine, ejecuten OTRA VEZ:\nGuia Frailejon > Activar IA (nivel completo)", "OK");
                return;
            }

            // ---- Paso 2: componentes ----
            var sistema = GameObject.Find("Sistema");
            var cerebro = sistema != null ? sistema.GetComponent<GuideBrain>() : null;
            if (cerebro == null)
            {
                EditorUtility.DisplayDialog("Falta la escena",
                    "Abran la escena Assets/Scenes/GuiaFrailejon.unity (o ejecuten primero 'Armar escena completa') y vuelvan a intentar.", "OK");
                return;
            }

            Component llm = sistema.GetComponent(tLLM);
            if (llm == null) llm = sistema.AddComponent(tLLM);
            Component agente = sistema.GetComponent(tAgente);
            if (agente == null) agente = sistema.AddComponent(tAgente);

            AsignarMiembro(agente, "llm", llm);
            AsignarMiembro(agente, "systemPrompt", PromptSistema);

            var adaptador = sistema.GetComponent<LLMForUnityAdapter>();
            if (adaptador == null) adaptador = sistema.AddComponent<LLMForUnityAdapter>();
            if (!AsignarMiembro(adaptador, "agente", agente))
                Debug.LogWarning("[ActivarIA] No se pudo asignar 'Agente' en LLM For Unity Adapter. Arrastren el componente LLMAgent a ese campo a mano.");

            cerebro.llmComponente = adaptador;

            EditorUtility.SetDirty(sistema);
            EditorSceneManager.MarkSceneDirty(sistema.scene);
            EditorSceneManager.SaveScene(sistema.scene);
            Selection.activeGameObject = sistema;

            EditorUtility.DisplayDialog("Activar IA — paso 2 de 2",
                "IA conectada al objeto Sistema.\n\nFALTA UNA COSA (a mano): descargar el modelo.\n" +
                "1. En el Inspector de 'Sistema', busquen el componente LLM.\n" +
                "2. Denle 'Download model' y elijan un modelo de la sección de modelos pequeños (1–2B parámetros).\n" +
                "3. Esperen a que termine la descarga.\n\n" +
                "Luego denle Play y pregunten algo que no esté literal en las preguntas frecuentes.", "OK");
        }

        internal static Type BuscarTipo(string nombreCompleto) =>
            AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => { try { return a.GetType(nombreCompleto); } catch { return null; } })
                .FirstOrDefault(t => t != null);

        /// <summary>Asigna una propiedad o campo (publico o privado) buscando tambien en las clases base.</summary>
        internal static bool AsignarMiembro(object obj, string nombre, object valor)
        {
            const BindingFlags f = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            for (Type t = obj.GetType(); t != null; t = t.BaseType)
            {
                var p = t.GetProperty(nombre, f | BindingFlags.DeclaredOnly);
                if (p != null && p.CanWrite && p.PropertyType.IsInstanceOfType(valor))
                {
                    try { p.SetValue(obj, valor); return true; } catch { }
                }
                var c = t.GetField(nombre, f | BindingFlags.DeclaredOnly);
                if (c != null && c.FieldType.IsInstanceOfType(valor)) { c.SetValue(obj, valor); return true; }
            }
            Debug.Log($"[ActivarIA] '{nombre}' no encontrado en {obj.GetType().Name} (puede que el paquete lo asigne solo).");
            return false;
        }
    }
}
#endif
