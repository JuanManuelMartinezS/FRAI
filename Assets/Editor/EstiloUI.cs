#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace GuiaFrailejon.EditorTools
{
    /// <summary>
    /// Paleta y sprites de la interfaz, generados por codigo (sin arte externo):
    /// esquinas redondeadas, pildoras, sombras suaves, degradados e iconos vectoriales (SDF rasterizado).
    /// Los PNG se guardan en Assets/Materiales/UI y solo se generan la primera vez.
    /// Para regenerarlos: borren esa carpeta y vuelvan a "Armar escena completa".
    /// </summary>
    public static class EstiloUI
    {
        public const string Carpeta = "Assets/Materiales/UI";

        // ---------------- Paleta ----------------
        public static readonly Color Noche = new Color(0.035f, 0.10f, 0.22f, 0.86f);      // cristal azul noche
        public static readonly Color NocheSolida = new Color(0.035f, 0.09f, 0.20f, 0.96f);
        public static readonly Color Cristal = new Color(1f, 1f, 1f, 0.13f);               // cristal claro sobre oscuro
        public static readonly Color CristalBorde = new Color(1f, 1f, 1f, 0.22f);
        public static readonly Color Azul = new Color(0.16f, 0.52f, 1f);
        public static readonly Color AzulSuave = new Color(0.16f, 0.52f, 1f, 0.30f);
        public static readonly Color Ambar = new Color(1f, 0.79f, 0.16f);
        public static readonly Color Verde = new Color(0.22f, 0.78f, 0.42f);
        public static readonly Color Turquesa = new Color(0.09f, 0.64f, 0.52f);
        public static readonly Color Rojo = new Color(0.90f, 0.20f, 0.22f);
        public static readonly Color TextoSuave = new Color(0.76f, 0.86f, 1f);
        public static readonly Color TextoOscuro = new Color(0.10f, 0.12f, 0.16f);

        // ---------------- Sprites base ----------------
        /// <summary>Rectangulo con esquinas redondeadas (9-slice).</summary>
        public static Sprite Redondeado() => Generar("Redondeado", 128, new Vector4(44, 44, 44, 44), (x, y) =>
        {
            const float R = 40f, N = 128;
            float cx = Mathf.Clamp(x, R, N - R), cy = Mathf.Clamp(y, R, N - R);
            float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
            return Mathf.Clamp01(R - d + 0.5f);
        });

        /// <summary>Pildora / circulo (9-slice con radio maximo): sirve para botones redondos y chips.</summary>
        public static Sprite Pildora() => Generar("Pildora", 128, new Vector4(63, 63, 63, 63), (x, y) =>
        {
            float d = Mathf.Sqrt((x - 64) * (x - 64) + (y - 64) * (y - 64));
            return Mathf.Clamp01(63.5f - d + 0.5f);
        });

        /// <summary>Sombra suave para poner detras de tarjetas.</summary>
        public static Sprite Sombra() => Generar("Sombra", 160, new Vector4(70, 70, 70, 70), (x, y) =>
        {
            const float R = 40f, M = 30f, N = 160;
            float cx = Mathf.Clamp(x, M + R, N - M - R), cy = Mathf.Clamp(y, M + R, N - M - R);
            float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) - R;
            float t = Mathf.Clamp01(1f - d / M);
            return t * t * 0.55f;
        });

        /// <summary>Degradado vertical: opaco arriba, transparente abajo.</summary>
        public static Sprite Degradado() => Generar("Degradado", 64, Vector4.zero, (x, y) =>
        {
            float t = y / 64f;   // 0 abajo, 1 arriba
            return Mathf.SmoothStep(0, 1, t);
        });

        /// <summary>Brillo horizontal (linea de escaneo).</summary>
        public static Sprite Brillo() => Generar("Brillo", 128, Vector4.zero, (x, y) =>
        {
            float u = Mathf.Abs(x / 128f - 0.5f) * 2f, v = Mathf.Abs(y / 128f - 0.5f) * 2f;
            return Mathf.Clamp01(1 - u * u) * Mathf.Clamp01(1 - v * v * v);
        });

        // ---------------- Iconos (blancos; se tiñen con Image.color) ----------------
        public static Sprite Icono(string nombre)
        {
            Func<Vector2, float> sdf = nombre switch
            {
                "mic" => p => Min(Caja(p, V(.5f, .62f), V(.11f, .21f), .11f),
                                  Max(Anillo(p, V(.5f, .6f), .25f, .045f), p.y - .6f),
                                  Seg(p, V(.5f, .35f), V(.5f, .18f), .045f),
                                  Seg(p, V(.36f, .16f), V(.64f, .16f), .045f)),
                "enviar" => p => Min(Poli(p, V(.14f, .82f), V(.27f, .5f), V(.9f, .5f)),
                                     Poli(p, V(.14f, .18f), V(.9f, .5f), V(.27f, .5f))) - .015f,
                "silencio" => p => Min(Caja(p, V(.22f, .5f), V(.08f, .12f), .03f),
                                       Poli(p, V(.26f, .38f), V(.46f, .18f), V(.46f, .82f), V(.26f, .62f)) - .02f,
                                       Seg(p, V(.6f, .38f), V(.84f, .62f), .045f),
                                       Seg(p, V(.6f, .62f), V(.84f, .38f), .045f)),
                "camara" => p => Max(Min(Caja(p, V(.5f, .44f), V(.38f, .26f), .08f), Caja(p, V(.5f, .72f), V(.13f, .07f), .04f)),
                                     -Max(Circ(p, V(.5f, .44f), .17f), -Circ(p, V(.5f, .44f), .1f))),
                "chat" => p => Max(Min(Caja(p, V(.5f, .56f), V(.38f, .27f), .13f), Poli(p, V(.24f, .2f), V(.42f, .34f), V(.26f, .34f))),
                                   -Min(Circ(p, V(.34f, .56f), .05f), Circ(p, V(.5f, .56f), .05f), Circ(p, V(.66f, .56f), .05f))),
                "nube" => p => Min(Circ(p, V(.36f, .46f), .16f), Circ(p, V(.56f, .56f), .21f), Circ(p, V(.74f, .44f), .14f),
                                   Caja(p, V(.55f, .38f), V(.27f, .08f), .08f)),
                "sol" => p => Min(Circ(p, V(.5f, .5f), .19f), Rayos(p, V(.5f, .5f), .29f, .42f, .04f)),
                "solnube" => p => Min(Max(Min(Circ(p, V(.42f, .62f), .15f), Rayos(p, V(.42f, .62f), .22f, .31f, .035f)),
                                          -(Min(Circ(p, V(.46f, .38f), .19f), Circ(p, V(.64f, .46f), .22f), Circ(p, V(.8f, .36f), .16f)) - .05f)),
                                      Min(Circ(p, V(.46f, .38f), .15f), Circ(p, V(.64f, .46f), .18f), Circ(p, V(.8f, .36f), .12f),
                                          Caja(p, V(.64f, .29f), V(.22f, .06f), .06f))),
                "lluvia" => p => Min(Circ(p, V(.36f, .58f), .15f), Circ(p, V(.56f, .66f), .19f), Circ(p, V(.74f, .56f), .13f),
                                     Caja(p, V(.55f, .5f), V(.27f, .07f), .07f),
                                     Seg(p, V(.36f, .3f), V(.3f, .14f), .035f), Seg(p, V(.54f, .3f), V(.48f, .14f), .035f),
                                     Seg(p, V(.72f, .3f), V(.66f, .14f), .035f)),
                "niebla" => p => Min(Seg(p, V(.18f, .66f), V(.82f, .66f), .045f), Seg(p, V(.26f, .5f), V(.74f, .5f), .045f),
                                     Seg(p, V(.18f, .34f), V(.82f, .34f), .045f)),
                "gota" => p => Min(Circ(p, V(.5f, .36f), .22f), Poli(p, V(.3f, .46f), V(.5f, .88f), V(.7f, .46f))),
                "viento" => p => Min(Seg(p, V(.12f, .64f), V(.66f, .64f), .045f), Max(Anillo(p, V(.66f, .74f), .1f, .045f), .66f - p.x),
                                     Seg(p, V(.12f, .46f), V(.8f, .46f), .045f),
                                     Seg(p, V(.12f, .28f), V(.56f, .28f), .045f)),
                "presion" => p => Min(Max(Anillo(p, V(.5f, .45f), .33f, .05f), -(p.y - .3f)), Seg(p, V(.5f, .45f), V(.68f, .64f), .045f),
                                      Circ(p, V(.5f, .45f), .07f)),
                "termometro" => p => Min(Seg(p, V(.5f, .8f), V(.5f, .38f), .08f), Circ(p, V(.5f, .26f), .14f)),
                "antena" => p => Min(Seg(p, V(.5f, .55f), V(.5f, .12f), .045f), Seg(p, V(.34f, .12f), V(.66f, .12f), .045f),
                                     Circ(p, V(.5f, .62f), .07f),
                                     Max(Anillo(p, V(.5f, .62f), .2f, .04f), .1f - Mathf.Abs(p.x - .5f)),
                                     Max(Anillo(p, V(.5f, .62f), .33f, .04f), .16f - Mathf.Abs(p.x - .5f))),
                "pin" => p => Max(Min(Circ(p, V(.5f, .62f), .26f), Poli(p, V(.28f, .52f), V(.5f, .1f), V(.72f, .52f))),
                                  -Circ(p, V(.5f, .62f), .1f)),
                "alerta" => p => Max(Poli(p, V(.5f, .9f), V(.06f, .12f), V(.94f, .12f)) - .02f,
                                     -Min(Seg(p, V(.5f, .64f), V(.5f, .42f), .055f), Circ(p, V(.5f, .28f), .055f))),
                "cerrar" => p => Min(Seg(p, V(.25f, .25f), V(.75f, .75f), .055f), Seg(p, V(.25f, .75f), V(.75f, .25f), .055f)),
                "atras" => p => Min(Seg(p, V(.26f, .5f), V(.8f, .5f), .05f), Seg(p, V(.26f, .5f), V(.48f, .72f), .05f),
                                    Seg(p, V(.26f, .5f), V(.48f, .28f), .05f)),
                "chevron" => p => Min(Seg(p, V(.26f, .62f), V(.5f, .38f), .06f), Seg(p, V(.5f, .38f), V(.74f, .62f), .06f)),
                "hoja" => p => Min(Max(Circ(p, V(.78f, .22f), .58f), Circ(p, V(.22f, .78f), .58f)), Seg(p, V(.12f, .12f), V(.36f, .36f), .04f)),
                "huella" => p => Min(Circ(p, V(.56f, .84f), .09f), Seg(p, V(.53f, .68f), V(.47f, .44f), .07f),
                                     Seg(p, V(.47f, .44f), V(.36f, .14f), .055f), Seg(p, V(.47f, .44f), V(.64f, .2f), .055f),
                                     Seg(p, V(.53f, .64f), V(.36f, .5f), .045f), Seg(p, V(.53f, .64f), V(.7f, .54f), .045f)),
                "objetivo" => p => Min(Anillo(p, V(.5f, .5f), .27f, .05f), Circ(p, V(.5f, .5f), .09f),
                                       Seg(p, V(.5f, .07f), V(.5f, .17f), .045f), Seg(p, V(.5f, .83f), V(.5f, .93f), .045f),
                                       Seg(p, V(.07f, .5f), V(.17f, .5f), .045f), Seg(p, V(.83f, .5f), V(.93f, .5f), .045f)),
                "check" => p => Min(Seg(p, V(.22f, .5f), V(.42f, .3f), .06f), Seg(p, V(.42f, .3f), V(.8f, .7f), .06f)),
                "info" => p => Min(Seg(p, V(.5f, .62f), V(.5f, .22f), .07f), Circ(p, V(.5f, .8f), .08f)),
                "montana" => p => Max(Min(Poli(p, V(.02f, .14f), V(.38f, .82f), V(.74f, .14f)), Poli(p, V(.42f, .14f), V(.68f, .6f), V(.98f, .14f))),
                                      -Min(Seg(p, V(.25f, .56f), V(.33f, .5f), .025f), Seg(p, V(.33f, .5f), V(.4f, .56f), .025f),
                                           Seg(p, V(.4f, .56f), V(.5f, .48f), .025f))),
                "esquina" => p => Min(Seg(p, V(.1f, .9f), V(.1f, .35f), .07f), Seg(p, V(.1f, .9f), V(.65f, .9f), .07f)),
                _ => throw new ArgumentException("Icono desconocido: " + nombre),
            };
            const int N = 128;
            return Generar("Icono_" + nombre, N, Vector4.zero, (x, y) =>
            {
                float d = sdf(new Vector2(x / N, y / N));
                return Mathf.Clamp01(0.5f - d * N);
            });
        }

        // ---------------- Funciones de distancia (coordenadas 0..1, y hacia arriba) ----------------
        static Vector2 V(float x, float y) => new Vector2(x, y);
        static float Min(params float[] d) { float m = float.MaxValue; foreach (var v in d) m = Mathf.Min(m, v); return m; }
        static float Max(float a, float b) => Mathf.Max(a, b);
        static float Circ(Vector2 p, Vector2 c, float r) => (p - c).magnitude - r;
        static float Anillo(Vector2 p, Vector2 c, float r, float w) => Mathf.Abs((p - c).magnitude - r) - w;

        static float Seg(Vector2 p, Vector2 a, Vector2 b, float r)
        {
            Vector2 pa = p - a, ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
            return (pa - ba * h).magnitude - r;
        }

        static float Caja(Vector2 p, Vector2 c, Vector2 medio, float r)
        {
            Vector2 q = new Vector2(Mathf.Abs(p.x - c.x), Mathf.Abs(p.y - c.y)) - medio + new Vector2(r, r);
            return new Vector2(Mathf.Max(q.x, 0), Mathf.Max(q.y, 0)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0) - r;
        }

        /// <summary>Poligono convexo (vertices en cualquier sentido).</summary>
        static float Poli(Vector2 p, params Vector2[] v)
        {
            float area = 0;
            for (int i = 0; i < v.Length; i++) { var a = v[i]; var b = v[(i + 1) % v.Length]; area += a.x * b.y - b.x * a.y; }
            float s = area > 0 ? 1 : -1, dentro = float.MinValue, fuera = float.MaxValue;
            bool afuera = false;
            for (int i = 0; i < v.Length; i++)
            {
                Vector2 a = v[i], b = v[(i + 1) % v.Length], e = b - a;
                Vector2 n = new Vector2(e.y, -e.x).normalized * s;   // normal hacia afuera
                float lado = Vector2.Dot(p - a, n);
                dentro = Mathf.Max(dentro, lado);
                if (lado > 0) afuera = true;
                fuera = Mathf.Min(fuera, Seg(p, a, b, 0));
            }
            return afuera ? fuera : dentro;
        }

        static float Rayos(Vector2 p, Vector2 c, float r0, float r1, float w)
        {
            float d = float.MaxValue;
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                d = Mathf.Min(d, Seg(p, c + dir * r0, c + dir * r1, w));
            }
            return d;
        }

        // ---------------- Generacion de PNG ----------------
        static Sprite Generar(string nombre, int n, Vector4 borde, Func<float, float, float> alfa)
        {
            string ruta = $"{Carpeta}/{nombre}.png";
            if (!File.Exists(ruta))
            {
                if (!AssetDatabase.IsValidFolder(Carpeta))
                {
                    if (!AssetDatabase.IsValidFolder("Assets/Materiales")) AssetDatabase.CreateFolder("Assets", "Materiales");
                    AssetDatabase.CreateFolder("Assets/Materiales", "UI");
                }
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                        px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(alfa(x + 0.5f, y + 0.5f)) * 255));
                tex.SetPixels32(px);
                File.WriteAllBytes(ruta, tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(ruta, ImportAssetOptions.ForceUpdate);
                var imp = (TextureImporter)AssetImporter.GetAtPath(ruta);
                imp.textureType = TextureImporterType.Sprite;
                imp.spriteImportMode = SpriteImportMode.Single;
                imp.spriteBorder = borde;
                imp.mipmapEnabled = false;
                imp.alphaIsTransparency = true;
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.textureCompression = TextureImporterCompression.Uncompressed;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(ruta);
        }
    }
}
#endif
