using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GuiaFrailejon
{
    /// <summary>
    /// Barra de navegacion inferior (Guia / Explorar / Clima / Recursos).
    /// Cada pestana muestra sus objetos y oculta los de las demas.
    /// </summary>
    public class NavegacionUI : MonoBehaviour
    {
        public const int Guia = 0, Explorar = 1, Clima = 2, Recursos = 3;

        [Serializable]
        public class Pestana
        {
            public string nombre;
            public Button boton;
            public Image icono;
            public TMP_Text texto;
            [Tooltip("Pildora que resalta la pestana activa.")]
            public Image resaltado;
            [Tooltip("Puntito rojo de aviso (p. ej. alerta de clima).")]
            public GameObject insignia;
            public GameObject[] contenido;
        }

        public Pestana[] pestanas;
        public Color colorActivo = Color.white;
        public Color colorInactivo = new Color(0.76f, 0.86f, 1f, 0.65f);

        public int Actual { get; private set; } = -1;
        public event Action<int> AlCambiar;

        void Awake()
        {
            for (int i = 0; i < pestanas.Length; i++)
            {
                int idx = i;
                if (pestanas[i].boton) pestanas[i].boton.onClick.AddListener(() => Ir(idx));
                if (pestanas[i].insignia) pestanas[i].insignia.SetActive(false);
            }
        }

        void Start() { if (Actual < 0) Ir(Guia); }

        public void Ir(int indice)
        {
            if (indice < 0 || indice >= pestanas.Length) return;
            bool cambio = indice != Actual;
            Actual = indice;

            // Primero se oculta todo y luego se muestra lo de la pestana (por si comparten objetos).
            for (int i = 0; i < pestanas.Length; i++)
                if (i != indice && pestanas[i].contenido != null)
                    foreach (var go in pestanas[i].contenido) if (go) go.SetActive(false);
            if (pestanas[indice].contenido != null)
                foreach (var go in pestanas[indice].contenido) if (go) go.SetActive(true);

            for (int i = 0; i < pestanas.Length; i++)
            {
                var p = pestanas[i];
                bool activa = i == indice;
                if (p.icono) p.icono.color = activa ? colorActivo : colorInactivo;
                if (p.texto)
                {
                    p.texto.color = activa ? colorActivo : colorInactivo;
                    p.texto.fontStyle = activa ? FontStyles.Bold : FontStyles.Normal;
                }
                if (p.resaltado) p.resaltado.enabled = activa;
            }
            if (cambio) AlCambiar?.Invoke(indice);
        }

        public void Insignia(int indice, bool visible)
        {
            if (indice < 0 || indice >= pestanas.Length || pestanas[indice].insignia == null) return;
            pestanas[indice].insignia.SetActive(visible && indice != Actual);
        }
    }
}
