using System;
using System.Threading.Tasks;
using UnityEngine;
#if GUIA_LLMUNITY
using LLMUnity;
#endif

namespace GuiaFrailejon
{
    /// <summary>
    /// Adaptador para el paquete "LLM for Unity" (undreamai, corre llama.cpp dentro del celular).
    /// Se activa con el menu  Guia Frailejon > Activar IA  (agrega el simbolo GUIA_LLMUNITY y conecta todo).
    /// Sin el simbolo, la app compila y funciona solo en nivel basico.
    /// </summary>
    public class LLMForUnityAdapter : MonoBehaviour, ILocalLLM
    {
#if GUIA_LLMUNITY
        [Tooltip("Componente LLMAgent del paquete LLM for Unity.")]
        public LLMAgent agente;

        bool modeloListo;

        async void Start()
        {
            try
            {
                // Espera a que el modelo quede cargado (o descargado, si usan 'Download on Build').
                modeloListo = await LLM.WaitUntilModelSetup();
                Debug.Log("[IA] Modelo listo: " + modeloListo);
            }
            catch (Exception e) { Debug.LogWarning("[IA] No se pudo preparar el modelo: " + e.Message); }
        }

        public bool Disponible => agente != null && agente.isActiveAndEnabled && modeloListo;

        public async Task<string> Generar(string prompt, Action<string> alAvanzar = null)
        {
            if (!Disponible) throw new InvalidOperationException("LLM no disponible");
            // false = no guardar en el historial: cada pregunta es independiente y no se acumula memoria.
            return await agente.Chat(prompt, parcial => alAvanzar?.Invoke(parcial), null, false);
        }

        public void Cancelar() { if (agente != null) agente.CancelRequests(); }
#else
        public bool Disponible => false;
        public Task<string> Generar(string prompt, Action<string> alAvanzar = null) =>
            Task.FromException<string>(new InvalidOperationException("Compilado sin GUIA_LLMUNITY"));
        public void Cancelar() { }
#endif
    }
}
