using System;
using System.Threading.Tasks;

namespace GuiaFrailejon
{
    /// <summary>
    /// Contrato del modelo de lenguaje local. Asi pueden cambiar de motor
    /// (LLM for Unity / llama.cpp, MediaPipe, etc.) sin tocar el resto de la app.
    /// </summary>
    public interface ILocalLLM
    {
        bool Disponible { get; }
        /// <param name="alAvanzar">Texto acumulado mientras se genera (para mostrarlo en vivo).</param>
        Task<string> Generar(string prompt, Action<string> alAvanzar = null);
        void Cancelar();
    }
}
