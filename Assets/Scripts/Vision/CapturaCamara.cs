using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

namespace GuiaFrailejon
{
    /// <summary>
    /// Entrega cuadros de la camara trasera para la vision artificial.
    /// - Con AR: lee la imagen de la camara de ARCore (sin abrir otra camara y sin los objetos 3D encima).
    /// - Sin AR (o en el editor): usa WebCamTexture y ademas la muestra en pantalla (fondoCamara).
    /// </summary>
    public class CapturaCamara : MonoBehaviour
    {
        public ARCameraManager camaraAR;
        public ARAvailability disponibilidadAR;
        [Tooltip("Donde se muestra la camara cuando NO hay AR (en AR ya se ve el fondo de ARCore).")]
        public UnityEngine.UI.RawImage fondoCamara;
        [Tooltip("Correccion extra de giro (grados) si la imagen que ve el modelo sale de lado.")]
        public int ajusteGiroAR = 0;
        [Tooltip("Ancho maximo del cuadro que se procesa (menos = mas rapido).")]
        public int anchoMaximo = 640;

        WebCamTexture web;
        Texture2D texAR;
        Color32[] buffer;

        public bool UsaAR => camaraAR != null && camaraAR.isActiveAndEnabled && (disponibilidadAR == null || disponibilidadAR.ModoAR);
        public bool Activa { get; private set; }
        public string Error { get; private set; } = "";

        public void Iniciar()
        {
            Activa = true;
            Error = "";
            if (UsaAR) { if (fondoCamara) fondoCamara.gameObject.SetActive(false); return; }

#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.Camera)) Permission.RequestUserPermission(Permission.Camera);
#endif
            if (web == null)
            {
                var dispositivos = WebCamTexture.devices;
                if (dispositivos.Length == 0) { Error = "No se encontró ninguna cámara."; return; }
                string nombre = dispositivos[0].name;
                foreach (var d in dispositivos) if (!d.isFrontFacing) { nombre = d.name; break; }
                web = new WebCamTexture(nombre, 1280, 720, 30);
            }
            web.Play();
            if (fondoCamara) { fondoCamara.texture = web; fondoCamara.gameObject.SetActive(true); }
        }

        public void Detener()
        {
            Activa = false;
            if (web != null && web.isPlaying) web.Stop();
            if (fondoCamara) fondoCamara.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (web != null) { web.Stop(); Destroy(web); }
            if (texAR) Destroy(texAR);
        }

        void Update()
        {
            // Ajusta la vista de la webcam: giro + "cover" (llenar la pantalla sin deformar).
            if (web == null || !web.isPlaying || fondoCamara == null || web.width < 32) return;
            int giro = web.videoRotationAngle;
            var rt = fondoCamara.rectTransform;
            var padre = (RectTransform)rt.parent;
            bool rota = giro % 180 != 0;
            float w = rota ? web.height : web.width, h = rota ? web.width : web.height;
            float escala = Mathf.Max(padre.rect.width / w, padre.rect.height / h);
            rt.sizeDelta = rota ? new Vector2(h * escala, w * escala) : new Vector2(w * escala, h * escala);
            rt.localEulerAngles = new Vector3(0, 0, -giro);
            fondoCamara.uvRect = web.videoVerticallyMirrored ? new Rect(0, 1, 1, -1) : new Rect(0, 0, 1, 1);
        }

        /// <summary>
        /// Copia el ultimo cuadro. 'giro' = grados horarios para dejarlo derecho.
        /// Devuelve false si todavia no hay imagen.
        /// </summary>
        public bool TomarCuadro(out Color32[] pixeles, out int ancho, out int alto, out int giro)
        {
            pixeles = null; ancho = alto = giro = 0;
            if (!Activa) return false;
            return UsaAR ? CuadroAR(out pixeles, out ancho, out alto, out giro)
                         : CuadroWeb(out pixeles, out ancho, out alto, out giro);
        }

        bool CuadroWeb(out Color32[] px, out int w, out int h, out int giro)
        {
            px = null; w = h = giro = 0;
            if (web == null || !web.isPlaying || !web.didUpdateThisFrame && buffer == null || web.width < 32) return false;
            w = web.width; h = web.height;
            if (buffer == null || buffer.Length != w * h) buffer = new Color32[w * h];
            web.GetPixels32(buffer);
            px = buffer;
            giro = web.videoRotationAngle;
            if (web.videoVerticallyMirrored) Voltear(px, w, h);
            return true;
        }

        bool CuadroAR(out Color32[] px, out int w, out int h, out int giro)
        {
            px = null; w = h = giro = 0;
            if (!camaraAR.TryAcquireLatestCpuImage(out XRCpuImage img)) return false;
            try
            {
                int div = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(img.width, img.height) / (float)anchoMaximo));
                var dims = new Vector2Int(img.width / div, img.height / div);
                var p = new XRCpuImage.ConversionParams(img, TextureFormat.RGBA32, XRCpuImage.Transformation.MirrorY)
                { outputDimensions = dims };
                if (texAR == null || texAR.width != dims.x || texAR.height != dims.y)
                {
                    if (texAR) Destroy(texAR);
                    texAR = new Texture2D(dims.x, dims.y, TextureFormat.RGBA32, false);
                }
                NativeArray<byte> datos = texAR.GetRawTextureData<byte>();
                img.Convert(p, datos);
                texAR.Apply(false);
                px = texAR.GetPixels32();
                w = dims.x; h = dims.y;
            }
            catch (Exception e)
            {
                Error = "No se pudo leer la cámara AR.";
                Debug.LogWarning("[Vision] " + e.Message);
                return false;
            }
            finally { img.Dispose(); }

            // El sensor de la camara trasera es apaisado: en vertical hay que girarlo 90° horario.
            giro = Screen.orientation switch
            {
                ScreenOrientation.Portrait => 90,
                ScreenOrientation.PortraitUpsideDown => 270,
                ScreenOrientation.LandscapeRight => 180,
                _ => 0,
            } + ajusteGiroAR;
            return true;
        }

        static void Voltear(Color32[] px, int w, int h)
        {
            for (int y = 0; y < h / 2; y++)
                for (int x = 0; x < w; x++)
                {
                    int a = y * w + x, b = (h - 1 - y) * w + x;
                    (px[a], px[b]) = (px[b], px[a]);
                }
        }
    }
}
