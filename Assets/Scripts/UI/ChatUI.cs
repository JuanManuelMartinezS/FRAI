using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GuiaFrailejon
{
    /// <summary>
    /// Conversacion en burbujas, tipo WhatsApp. Las burbujas se crean por codigo (no necesita prefabs).
    /// Va en el objeto "Chat", que tiene un ScrollRect.
    /// </summary>
    public class ChatUI : MonoBehaviour
    {
        public ScrollRect scroll;
        public RectTransform contenido;
        public Sprite spriteBurbuja;
        public float anchoBurbuja = 820;
        public int maxMensajes = 40;

        public Color colorFrai = new Color(0.93f, 0.97f, 1f, 0.97f);
        public Color colorUsuario = new Color(0.13f, 0.50f, 0.98f, 0.97f);
        public Color colorSeguridad = new Color(0.80f, 0.16f, 0.18f, 0.97f);
        public Color colorNota = new Color(1f, 1f, 1f, 0.14f);

        [Header("Avatar de Frai (opcional)")]
        public Sprite spriteAvatarFondo;
        public Sprite spriteAvatarIcono;
        public Color colorAvatar = new Color(0.09f, 0.64f, 0.52f);
        public float tamAvatar = 72;
        public float tamTexto = 32;

        public class Burbuja { public GameObject fila; public Image fondo; public TMP_Text texto; public TMP_Text fuente; public bool claro; }

        public enum Estilo { Frai, Usuario, Seguridad, Nota }

        public Burbuja Agregar(string texto, Estilo estilo, string fuente = null)
        {
            bool usuario = estilo == Estilo.Usuario;
            var fila = new GameObject("Fila", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            fila.transform.SetParent(contenido, false);
            var h = fila.GetComponent<HorizontalLayoutGroup>();
            h.childAlignment = usuario ? TextAnchor.UpperRight : TextAnchor.UpperLeft;
            h.childControlWidth = false; h.childControlHeight = true;
            h.childForceExpandWidth = false; h.childForceExpandHeight = false;
            h.spacing = 14;

            bool avatar = spriteAvatarFondo && (estilo == Estilo.Frai || estilo == Estilo.Seguridad);
            if (avatar) CrearAvatar(fila.transform);

            var go = new GameObject("Burbuja", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            go.transform.SetParent(fila.transform, false);
            float ancho = estilo == Estilo.Nota ? anchoBurbuja * 0.9f : estilo == Estilo.Usuario ? anchoBurbuja * 0.85f : anchoBurbuja;
            if (avatar) ancho -= tamAvatar + h.spacing;
            ((RectTransform)go.transform).sizeDelta = new Vector2(ancho, 100);
            var img = go.GetComponent<Image>();
            img.sprite = spriteBurbuja;
            img.type = Image.Type.Sliced;
            img.raycastTarget = false;
            img.color = ColorDe(estilo);
            var v = go.GetComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(28, 28, 18, 18);
            v.spacing = 6;
            v.childControlWidth = true; v.childControlHeight = true;
            v.childForceExpandWidth = true; v.childForceExpandHeight = false;

            bool claro = estilo != Estilo.Frai;   // texto blanco sobre fondo oscuro
            var b = new Burbuja { fila = fila, fondo = img, claro = claro };
            b.texto = CrearTexto(go.transform, texto, estilo == Estilo.Nota ? tamTexto * 0.8f : tamTexto, claro ? Color.white : new Color(0.10f, 0.12f, 0.16f));
            if (!string.IsNullOrEmpty(fuente)) b.fuente = CrearFuente(go.transform, fuente, claro);

            Limitar();
            Bajar();
            return b;
        }

        public void Actualizar(Burbuja b, string texto, string fuente = null, Estilo? estilo = null)
        {
            if (b == null || b.texto == null) return;
            b.texto.text = texto;
            if (estilo.HasValue)
            {
                b.fondo.color = ColorDe(estilo.Value);
                b.claro = estilo.Value != Estilo.Frai;
                b.texto.color = b.claro ? Color.white : new Color(0.12f, 0.12f, 0.12f);
            }
            if (!string.IsNullOrEmpty(fuente))
            {
                if (b.fuente == null) b.fuente = CrearFuente(b.fondo.transform, fuente, b.claro);
                else b.fuente.text = fuente;
            }
            Bajar();
        }

        // Si el chat esta oculto (plegado u otra pestana) no se pueden iniciar corrutinas: se baja al volver a mostrarse.
        bool bajarPendiente;
        void Bajar()
        {
            if (isActiveAndEnabled) StartCoroutine(BajarAlFinal());
            else bajarPendiente = true;
        }

        void OnEnable()
        {
            if (!bajarPendiente) return;
            bajarPendiente = false;
            StartCoroutine(BajarAlFinal());
        }

        public void Limpiar()
        {
            for (int i = contenido.childCount - 1; i >= 0; i--) Destroy(contenido.GetChild(i).gameObject);
        }

        void CrearAvatar(Transform fila)
        {
            var go = new GameObject("Avatar", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(fila, false);
            ((RectTransform)go.transform).sizeDelta = new Vector2(tamAvatar, tamAvatar);
            var le = go.GetComponent<LayoutElement>();
            le.preferredHeight = le.minHeight = tamAvatar;
            var img = go.GetComponent<Image>();
            img.sprite = spriteAvatarFondo; img.color = colorAvatar; img.raycastTarget = false;
            if (spriteAvatarIcono == null) return;
            var ico = new GameObject("Icono", typeof(RectTransform), typeof(Image));
            ico.transform.SetParent(go.transform, false);
            ((RectTransform)ico.transform).sizeDelta = new Vector2(tamAvatar * 0.6f, tamAvatar * 0.6f);
            var ii = ico.GetComponent<Image>();
            ii.sprite = spriteAvatarIcono; ii.raycastTarget = false;
        }

        Color ColorDe(Estilo e) => e switch
        {
            Estilo.Usuario => colorUsuario,
            Estilo.Seguridad => colorSeguridad,
            Estilo.Nota => colorNota,
            _ => colorFrai
        };

        static TMP_Text CrearTexto(Transform padre, string texto, float tam, Color color)
        {
            var go = new GameObject("Texto", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(padre, false);
            var t = go.GetComponent<TextMeshProUGUI>();
            t.text = texto;
            t.fontSize = tam;
            t.color = color;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.raycastTarget = false;
            return t;
        }

        static TMP_Text CrearFuente(Transform padre, string fuente, bool claro) =>
            CrearTexto(padre, "Fuente: " + fuente, 21, claro ? new Color(1, 1, 1, 0.75f) : new Color(0.38f, 0.38f, 0.38f));

        void Limitar()
        {
            int extra = contenido.childCount - maxMensajes;
            for (int i = 0; i < extra; i++) Destroy(contenido.GetChild(i).gameObject);
        }

        IEnumerator BajarAlFinal()
        {
            yield return null;
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(contenido);
            if (scroll) scroll.verticalNormalizedPosition = 0f;
        }
    }
}
