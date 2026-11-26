using System;
using UnityEngine;

namespace GuiaFrailejon
{
    public enum Avatar { Frailejon = 0, Condor = 1 }

    /// <summary>Que personaje esta activo (frailejon o condor). Se guarda entre sesiones.</summary>
    public static class AvatarSeleccion
    {
        const string Clave = "avatar_guia";
        static Avatar? actual;

        public static event Action Cambio;

        public static Avatar Actual
        {
            get
            {
                if (actual == null) actual = (Avatar)PlayerPrefs.GetInt(Clave, 0);
                return actual.Value;
            }
        }

        public static void Alternar() =>
            Establecer(Actual == Avatar.Frailejon ? Avatar.Condor : Avatar.Frailejon);

        public static void Establecer(Avatar a)
        {
            if (actual == a) return;
            actual = a;
            PlayerPrefs.SetInt(Clave, (int)a);
            PlayerPrefs.Save();
            Cambio?.Invoke();
        }
    }
}
