#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.Build;

namespace GuiaFrailejon.EditorTools
{
    /// <summary>
    /// Activa el simbolo GUIA_GALERIA (boton "Subir foto" de Explorar) cuando el paquete NativeGallery esta instalado,
    /// y lo quita si no lo esta. Asi el proyecto compila con o sin el paquete.
    /// </summary>
    [InitializeOnLoad]
    public static class AutoGaleria
    {
        const string Simbolo = "GUIA_GALERIA";

        static AutoGaleria()
        {
            bool hay = ActivarIA.BuscarTipo("NativeGallery") != null;
            Ajustar(NamedBuildTarget.Android, hay);
            Ajustar(NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup), hay);
        }

        static void Ajustar(NamedBuildTarget t, bool activar)
        {
            string actual = PlayerSettings.GetScriptingDefineSymbols(t);
            var lista = actual.Split(';').Where(x => x.Length > 0).ToList();
            bool tiene = lista.Contains(Simbolo);
            if (tiene == activar) return;
            if (activar) lista.Add(Simbolo); else lista.Remove(Simbolo);
            PlayerSettings.SetScriptingDefineSymbols(t, string.Join(";", lista));
        }
    }
}
#endif
