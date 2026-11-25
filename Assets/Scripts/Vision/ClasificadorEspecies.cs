using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
#if GUIA_VISION
using Unity.InferenceEngine;
#endif

namespace GuiaFrailejon
{
    // ---- vision.json (mismo formato que genera packages/ml-vision-pipeline) ----
    [Serializable]
    public class EntradaModelo
    {
        public string name = "pixels";
        public int size = 224;
        // Opcionales: si 'mean'/'std' vienen, la normalizacion (px-mean)/std se hace aqui y no en el grafo.
        public float[] mean;
        public float[] std;
        // Fraccion del lado corto que se recorta (size/resize). 0 = usar CropPct por defecto (0.95).
        public float cropPct;
    }
    [Serializable] public class SalidaModelo { public string name = "logits"; }
    [Serializable]
    public class EtiquetaEspecie
    {
        public int index;
        public long taxonKey;
        public string scientificName, kingdom, order, family, genus;
        public string @class;
    }
    [Serializable]
    public class MetaVision
    {
        public int schema;
        public bool demo;
        public string architecture;
        public EntradaModelo input = new EntradaModelo();
        public SalidaModelo output = new SalidaModelo();
        public float temperature = 1f;
        public float rejectThreshold = 0.8f;
        public EtiquetaEspecie[] labels = new EtiquetaEspecie[0];
    }

    // ---- especies_info.json (fichas para mostrar al visitante) ----
    [Serializable]
    public class FichaEspecie
    {
        public string cientifico, comun, tipo, descripcion, cuidado, fuente;
    }
    [Serializable] public class FichasEspecies { public FichaEspecie[] especies = new FichaEspecie[0]; }

    public enum TipoDecision { Identificada, Incierta, Demo }

    public struct Candidata
    {
        public EtiquetaEspecie etiqueta;
        public FichaEspecie ficha;
        public float prob;
    }

    public class DecisionVision
    {
        public TipoDecision tipo;
        public List<Candidata> candidatas = new List<Candidata>();
        public Candidata Mejor => candidatas.Count > 0 ? candidatas[0] : default;
    }

    /// <summary>
    /// Identifica especies con el modelo Student exportado a ONNX (Unity Inference Engine, 100 % en el celular).
    /// Preprocesado y decision IGUALES a la PWA (apps/web-pwa/src/core/ai-vision):
    ///   - lado corto -> floor(size/0.95), recorte central size x size, RGB NCHW en [0,1] (la normalizacion va en el grafo)
    ///   - softmax(logits / T); solo se afirma una especie si P(top-1) >= rejectThreshold
    ///   - si vision.json dice "demo": true, nunca se afirma nada.
    /// Requiere el paquete com.unity.ai.inference y el simbolo GUIA_VISION (menu Guia Frailejon > Activar vision artificial).
    /// </summary>
    public class ClasificadorEspecies : MonoBehaviour
    {
        public const float CropPct = 0.95f;

        [Tooltip("El .onnx importado (ModelAsset). Lo asigna el menu 'Activar vision artificial'.")]
        public UnityEngine.Object modelo;
        [Tooltip("vision.json: etiquetas, temperatura y umbral de rechazo.")]
        public TextAsset metadatos;
        [Tooltip("especies_info.json: nombre comun y descripcion de cada especie.")]
        public TextAsset fichas;
        [Tooltip("CPU es lo mas compatible. GPU puede ser mas rapido en celulares con Vulkan.")]
        public bool usarGPU = false;

        public MetaVision Meta { get; private set; }
        public bool Disponible { get; private set; }
        public string Motivo { get; private set; } = "Cargando…";
        public bool EsDemo => Meta != null && Meta.demo;
        public int Tamano => Meta != null && Meta.input.size > 0 ? Meta.input.size : 224;

        readonly Dictionary<string, FichaEspecie> porNombre = new Dictionary<string, FichaEspecie>(StringComparer.OrdinalIgnoreCase);
        bool ocupado;

#if GUIA_VISION
        Worker worker;
#endif

        void Awake()
        {
            try
            {
                Meta = metadatos ? JsonUtility.FromJson<MetaVision>(metadatos.text) : null;
                if (fichas)
                    foreach (var f in JsonUtility.FromJson<FichasEspecies>(fichas.text).especies)
                        if (!string.IsNullOrEmpty(f.cientifico)) porNombre[f.cientifico] = f;
            }
            catch (Exception e) { Debug.LogError("[Vision] JSON invalido: " + e.Message); }

            if (Meta == null || Meta.labels == null || Meta.labels.Length == 0) { Motivo = "Falta vision.json con las especies."; return; }
#if GUIA_VISION
            var asset = modelo as ModelAsset;
            if (asset == null) { Motivo = "Falta el modelo .onnx (menú Guia Frailejon > Activar visión artificial)."; return; }
            try
            {
                var m = ModelLoader.Load(asset);
                worker = new Worker(m, usarGPU && SystemInfo.supportsComputeShaders ? BackendType.GPUCompute : BackendType.CPU);
                Disponible = true;
                Motivo = "";
                Debug.Log($"[Vision] Modelo listo: {Meta.labels.Length} especies, {(usarGPU ? "GPU" : "CPU")}{(Meta.demo ? " (DEMO)" : "")}");
            }
            catch (Exception e)
            {
                Motivo = "No se pudo cargar el modelo de visión.";
                Debug.LogError("[Vision] " + e);
            }
#else
            Motivo = "La visión artificial no está activada en esta versión (menú Guia Frailejon > Activar visión artificial).";
#endif
        }

        void OnDestroy()
        {
#if GUIA_VISION
            worker?.Dispose();
#endif
        }

        /// <summary>Ejecuta el modelo. 'pixels' = NCHW [1,3,S,S] en [0,1]. Devuelve null si esta ocupado o no hay modelo.</summary>
        public async Task<float[]> Inferir(float[] pixels)
        {
            if (!Disponible || ocupado) return null;
            ocupado = true;
            try
            {
#if GUIA_VISION
                int s = Tamano;
                using var entrada = new Tensor<float>(new TensorShape(1, 3, s, s), pixels);
                worker.Schedule(entrada);
                var salida = worker.PeekOutput() as Tensor<float>;
                if (salida == null) return null;
                using var copia = await salida.ReadbackAndCloneAsync();
                return copia.DownloadToArray();
#else
                await Task.Yield();
                return null;
#endif
            }
            catch (Exception e)
            {
                Debug.LogError("[Vision] " + e.Message);
                return null;
            }
            finally { ocupado = false; }
        }

        // ------------------------------------------------------------------
        // Postprocesado (paridad con postprocess.ts)
        // ------------------------------------------------------------------
        public static float[] Softmax(float[] logits, float temperatura = 1f)
        {
            if (temperatura <= 0) temperatura = 1;
            var p = new float[logits.Length];
            float max = float.MinValue, suma = 0;
            for (int i = 0; i < p.Length; i++) max = Mathf.Max(max, logits[i] / temperatura);
            for (int i = 0; i < p.Length; i++) { p[i] = Mathf.Exp(logits[i] / temperatura - max); suma += p[i]; }
            for (int i = 0; i < p.Length; i++) p[i] /= suma;
            return p;
        }

        /// <summary>Decide a partir de probabilidades ya calibradas (y suavizadas).</summary>
        public DecisionVision Decidir(float[] probs, int k = 3)
        {
            var d = new DecisionVision();
            var idx = new List<int>();
            for (int i = 0; i < probs.Length && i < Meta.labels.Length; i++) idx.Add(i);
            idx.Sort((a, b) => probs[b].CompareTo(probs[a]));
            for (int i = 0; i < Mathf.Min(k, idx.Count); i++)
            {
                var et = Meta.labels[idx[i]];
                d.candidatas.Add(new Candidata { etiqueta = et, ficha = Ficha(et), prob = probs[idx[i]] });
            }
            d.tipo = Meta.demo ? TipoDecision.Demo
                   : d.candidatas.Count > 0 && d.candidatas[0].prob >= Meta.rejectThreshold ? TipoDecision.Identificada
                   : TipoDecision.Incierta;
            return d;
        }

        public FichaEspecie Ficha(EtiquetaEspecie et)
        {
            if (et == null) return null;
            if (porNombre.TryGetValue(et.scientificName ?? "", out var f)) return f;
            if (!string.IsNullOrEmpty(et.genus) && porNombre.TryGetValue(et.genus, out f)) return f;
            return new FichaEspecie
            {
                cientifico = et.scientificName,
                comun = et.scientificName,
                tipo = TipoPorTaxonomia(et),
            };
        }

        static string TipoPorTaxonomia(EtiquetaEspecie et) =>
            et.kingdom == "Plantae" ? "Planta" :
            et.@class == "Aves" ? "Ave" :
            et.@class == "Mammalia" ? "Mamífero" :
            et.@class == "Insecta" ? "Insecto" :
            et.kingdom == "Fungi" ? "Hongo" : "Especie";

        // ------------------------------------------------------------------
        // Preprocesado (paridad con preprocess.ts)
        // ------------------------------------------------------------------
        /// <summary>
        /// Convierte un cuadro de camara en el tensor del modelo y, opcionalmente, en una miniatura para mostrar.
        /// 'giro' = grados (multiplo de 90, horario) para dejar la imagen derecha.
        /// </summary>
        public static float[] Preprocesar(Color32[] px, int w, int h, int giro, int s, Color32[] miniatura = null,
                                          float[] mean = null, float[] std = null, float cropPct = 0f)
        {
            if (cropPct <= 0f) cropPct = CropPct;
            bool norm = mean != null && std != null && mean.Length >= 3 && std.Length >= 3;
            giro = ((giro % 360) + 360) % 360;
            bool rota = giro == 90 || giro == 270;
            int wr = rota ? h : w, hr = rota ? w : h;      // tamano de la imagen ya derecha

            // Region que, re-escalada a s, equivale a Resize(lado corto) + CenterCrop(s)
            int resizeTo = Mathf.FloorToInt(s / cropPct);
            float escala = resizeTo / (float)Mathf.Min(wr, hr);
            float lado = s / escala;
            float sx = (wr - lado) / 2f, sy = (hr - lado) / 2f;

            var salida = new float[3 * s * s];
            int plano = s * s;
            for (int y = 0; y < s; y++)
            {
                // y de la salida va de arriba (0) hacia abajo; las texturas de Unity tienen y=0 abajo.
                float vy = sy + (y + 0.5f) / s * lado;          // coordenada "desde arriba" en la imagen derecha
                float ur = 1f - vy / hr;                        // normalizada, y hacia arriba
                for (int x = 0; x < s; x++)
                {
                    float vx = (sx + (x + 0.5f) / s * lado) / wr;
                    // Deshacer el giro: punto en la imagen derecha -> punto en la imagen original (normalizado, y arriba)
                    float ox, oy;
                    switch (giro)
                    {
                        case 90: ox = 1f - ur; oy = vx; break;
                        case 180: ox = 1f - vx; oy = 1f - ur; break;
                        case 270: ox = ur; oy = 1f - vx; break;
                        default: ox = vx; oy = ur; break;
                    }
                    Color32 c = Bilineal(px, w, h, ox * w - 0.5f, oy * h - 0.5f);
                    int i = y * s + x;
                    float r = c.r / 255f, g = c.g / 255f, b = c.b / 255f;
                    if (norm) { r = (r - mean[0]) / std[0]; g = (g - mean[1]) / std[1]; b = (b - mean[2]) / std[2]; }
                    salida[i] = r;
                    salida[plano + i] = g;
                    salida[2 * plano + i] = b;
                    if (miniatura != null) miniatura[(s - 1 - y) * s + x] = c;
                }
            }
            return salida;
        }

        static Color32 Bilineal(Color32[] px, int w, int h, float x, float y)
        {
            x = Mathf.Clamp(x, 0, w - 1); y = Mathf.Clamp(y, 0, h - 1);
            int x0 = (int)x, y0 = (int)y, x1 = Mathf.Min(x0 + 1, w - 1), y1 = Mathf.Min(y0 + 1, h - 1);
            float fx = x - x0, fy = y - y0;
            Color32 a = px[y0 * w + x0], b = px[y0 * w + x1], c = px[y1 * w + x0], d = px[y1 * w + x1];
            byte L(byte p, byte q, byte r, byte t) =>
                (byte)Mathf.RoundToInt(Mathf.Lerp(Mathf.Lerp(p, q, fx), Mathf.Lerp(r, t, fx), fy));
            return new Color32(L(a.r, b.r, c.r, d.r), L(a.g, b.g, c.g, d.g), L(a.b, b.b, c.b, d.b), 255);
        }
    }
}
