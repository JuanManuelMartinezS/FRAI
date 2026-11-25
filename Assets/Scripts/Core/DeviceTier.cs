using UnityEngine;

namespace GuiaFrailejon
{
    public enum Nivel { Basico, Completo }

    /// <summary>Decide si el celular puede correr el modelo de lenguaje.</summary>
    public static class DeviceTier
    {
        public const int RamMinimaMB = 6000;   // ajustar tras pruebas reales
        const string ClaveForzar = "forzar_nivel_basico";

        public static Nivel Detectar(bool modeloDisponible)
        {
            if (PlayerPrefs.GetInt(ClaveForzar, 0) == 1) return Nivel.Basico;   // el usuario eligio "ahorrar bateria"
            if (!modeloDisponible) return Nivel.Basico;
            if (SystemInfo.systemMemorySize < RamMinimaMB) return Nivel.Basico;
            if (SystemInfo.batteryLevel >= 0 && SystemInfo.batteryLevel < 0.2f &&
                SystemInfo.batteryStatus != BatteryStatus.Charging) return Nivel.Basico;
            return Nivel.Completo;
        }

        public static void ForzarBasico(bool si) { PlayerPrefs.SetInt(ClaveForzar, si ? 1 : 0); PlayerPrefs.Save(); }
    }
}
