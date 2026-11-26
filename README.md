# FRAI — Guía AR del Frailejón (PNN Los Nevados)

App móvil (Unity / Android) que acompaña a los visitantes del **Parque Nacional Natural Los Nevados**
(ruta de 4 estaciones: Aguacerales, Arenales, Valle Lunar y Valle de las Tumbas). **Frai**, un frailejón
(o un cóndor) en realidad aumentada, responde preguntas, identifica especies con la cámara y alerta sobre el
clima. **Todo corre en el celular, sin internet**, porque en el páramo no hay señal.

## Probar la app

Para probarla **no hace falta compilar nada**: descarga el APK desde la sección
**[Releases](../../releases)** del repositorio, cópialo a un celular Android (ARM64) e instálalo
(habilita "Instalar apps de orígenes desconocidos" si el celular lo pide). Para compilarla tú mismo, mira
[Instalación en un PC nuevo](#instalación-en-un-pc-nuevo).

## Contenido

0. [Probar la app](#probar-la-app)
1. [Qué hace](#qué-hace)
2. [Principios de diseño](#principios-de-diseño)
3. [Arquitectura](#arquitectura)
4. [Estructura del repositorio](#estructura-del-repositorio)
5. [Instalación en un PC nuevo](#instalación-en-un-pc-nuevo)
6. [Menús del editor](#menús-del-editor)
7. [Módulos en detalle](#módulos-en-detalle)
8. [Base de conocimiento y actualización](#base-de-conocimiento-y-actualización)
9. [Licencia de visita (GPS / tiempo)](#licencia-de-visita-gps--tiempo)
10. [Clima y LoRa](#clima-y-lora)
11. [Archivos grandes (Git LFS)](#archivos-grandes-git-lfs)
12. [Estado y pendientes](#estado-y-pendientes)

## Qué hace

| Pestaña | Función |
|---|---|
| **Guía** | Estación actual, recorrido 1–4 con minimapa, chat con Frai (texto y voz), botones *Silenciar / Acompáñame / Ven aquí*, SOS |
| **Explorar** | Identifica especies con la cámara o una foto de galería (top‑3 con probabilidades). Modelo local |
| **Clima** | Lectura de la estación meteorológica (ESP32 + LoRa), alertas y recomendaciones |
| **Recursos** | Monitor de FPS, CPU, memoria, gráficos y batería (solo mide mientras está visible) |

Extras: modo **sin AR** para celulares sin ARCore, **selector de avatar** (frailejón o cóndor), **modo
acompañante** (Frai camina al lado del visitante usando ARCore) y **licencia de visita** que bloquea la app
fuera del área o pasado un tiempo.

## Principios de diseño

- **Offline primero.** LLM, voz, visión y base de conocimiento van dentro del APK.
- **Nada inventado.** Las respuestas salen de contenido oficial con **fuente** (guion y página). Lo marcado
  `[PENDIENTE]` nunca se lee ni se afirma.
- **Seguridad no la redacta la IA.** Preguntas de seguridad → texto fijo validado.
- **Degradación elegante.** Si el celular no aguanta el LLM, la app sigue con respuestas oficiales directas.
- **Honestidad en visión.** El modelo rechaza por debajo de un umbral y un modelo *demo* nunca afirma una especie.

## Arquitectura

### Flujo de una pregunta (`GuideBrain`)

```
Pregunta ──► 1. SafetyRouter ── coincide palabra clave ──► texto fijo validado (tipo Seguridad)
                  │ no
                  ▼
             2. Retriever (BM25, sin dependencias)
                  │ puntaje < umbralMinimo (0.15) ──► "No tengo ese dato oficial…"
                  │ pregunta frecuente con puntaje ≥ 0.55 ──► respuesta oficial tal cual
                  ▼
             3. Nivel COMPLETO (RAM ≥ 6 GB, modelo cargado, batería > 20 %)
                  │ LLM redacta SOLO con los pasajes recuperados (máx. 3 frases)
                  │ descarta si dice NO_SE, si tarda > 30 s o si inventa cifras
                  ▼
             4. Respaldo: respuesta oficial más cercana si puntaje ≥ 0.35, si no "sin dato"
```

`TipoRespuesta`: `Seguridad`, `Oficial`, `GeneradaConFuente`, `SinDato`, `Pendiente`. Solo se lee en voz alta
lo que no es `Pendiente`.

### Niveles de dispositivo (`DeviceTier`)

- **Básico**: solo búsqueda + respuestas oficiales (cualquier celular).
- **Completo**: añade el LLM local. El usuario puede forzar Básico ("ahorrar batería").

### Pila tecnológica

| Capa | Tecnología |
|---|---|
| Motor | Unity **6000.6.10f1**, URP, Android (ARM64) |
| AR | AR Foundation / ARCore 6.6.2 |
| LLM local | LLMUnity + LlamaLib v2.0.5 · `llama-3.2-1b-instruct-q4_k_m.gguf` |
| Voz → texto | whisper.unity · `ggml-base.bin` |
| Texto → voz | TTS nativo del celular (Android / iOS `GuiaTTS.mm`) |
| Visión | Unity Inference Engine 2.6.1 · EfficientNet‑B0 ONNX (FP32/FP16), 250 especies |
| Modelos 3D | glTFast (`.glb` del frailejón y el cóndor) |
| UI | uGUI + TextMeshPro, generada por código |

## Estructura del repositorio

```
Assets/
  Scripts/
    Core/     GuideBrain, Retriever (BM25), SafetyRouter, KnowledgeBase, TextNormalizer,
              DeviceTier, GeoLocalizador (GPS), LicenciaVisita, ContentUpdater, AvatarSeleccion
    LLM/      ILocalLLM, LLMForUnityAdapter
    Voice/    WhisperSTT (voz→texto), TextToSpeech (texto→voz)
    Vision/   ClasificadorEspecies, CapturaCamara, ExplorarUI
    AR/       FrailejonSpawner, FraiAcompanante, FrailejonAnimator, ModoSinAR, ARAvailability
    Clima/    FuenteClima, ClimaUI
    UI/       GuideUI, ChatUI, NavegacionUI, MiniMapa, RecursosUI, SelectorAvatar,
              AreaSegura, BotonMantener, SubirConTeclado
  Editor/     ArmarEscena, ActivarIA, ActivarVoz, ActivarVision, AutoGaleria, EstiloUI
  StreamingAssets/
    conocimiento/base_conocimiento.json   base oficial (estaciones, seguridad, preguntas, documentos)
    config_visita.json                    parámetros de la licencia de visita
    Whisper/ggml-base.bin                 (LFS)
    LlamaLib-v2.0.5/android-arm64/…       (LFS)
  Vision/     modelo_especies.onnx, vision.json, especies_info.json
  Scenes/     GuiaFrailejon.unity (se regenera por código)
  Marcadores/ imágenes de las 4 estaciones + LibreriaEstaciones.asset (image tracking)
  Condor/, Modelos/, Prefabs/, Materiales/UI/   assets 3D, prefabs e íconos
Tools/
  construir_base.py        fusiona fuentes en la base, valida y genera el manifest
  conocimiento/            fuentes: guiones UTON (JSON)
  publicar/                base_conocimiento.json + manifest.json listos para subir
```

## Instalación en un PC nuevo

1. `git lfs install` y luego `git clone` (descarga Whisper y la librería de Android).
2. Abrir con Unity **6000.6.10f1** y esperar la importación de paquetes.
3. Menú **Guia Frailejon > Armar escena completa**.
4. Descargar el modelo de IA: objeto **Sistema** → componente **LLM** → **Download model** →
   *Llama 3.2 1B Instruct (Q4_K_M)* (~770 MB, no cabe en GitHub).
5. Repetir **Armar escena completa**: el modelo queda asignado (`[ActivarIA] Modelo asignado: …`).
6. Compilar para Android (perfil en `Assets/Settings/Build Profiles`). Para solo probar la app, descarga el APK desde *Releases* (ver [Probar la app](#probar-la-app)).

Sin el modelo la app funciona igual, solo con respuestas oficiales directas.

## Menús del editor

| Menú (*Guia Frailejon >*) | Qué hace |
|---|---|
| **Armar escena completa** | Regenera `GuiaFrailejon.unity`: XR Origin, UI, sistema, íconos y sprites |
| **Activar IA (nivel completo)** | Conecta el GGUF al componente LLM |
| **Activar voz** | Conecta Whisper para hablarle a Frai |
| **Activar visión artificial** | 3 pasos: instala Inference Engine, agrega el símbolo `GUIA_VISION`, conecta el modelo (re‑ejecutar tras cada paso) |

Los sprites de `Assets/Materiales/UI` se regeneran borrando la carpeta y rearmando la escena.

## Módulos en detalle

### Visión artificial
- `vision.json` (mismo formato que produce `packages/ml-vision-pipeline` del repo `pnn-nevados-edge-ai`):
  entrada 224×224 NCHW, media/desv. ImageNet, `cropPct` 0.878, `rejectThreshold` 0.35, 250 etiquetas.
- Modelo actual **real** (`demo: false`): EfficientNet‑B0, val top‑1 69.5 % / top‑3 84.8 %.
- La identificación ocurre **solo al tomar o subir una foto** y muestra los 3 mejores candidatos.
- **Unity no carga INT8 QDQ** (`QuantizeLinear/DequantizeLinear`): usar ONNX **FP32 o FP16**.
- Nombres comunes y descripciones en `especies_info.json` (pendientes de validar con el PNN).

### Voz
- **STT**: Whisper base local (`WhisperSTT`), botón de mantener para hablar.
- **TTS**: voz del celular; requiere el paquete de voz en español (Android: Ajustes › Idioma › Salida de
  texto a voz › Google › Instalar datos de voz › Español).

### AR
- `FrailejonSpawner`: coloca a Frai al detectar el marcador de la estación (image tracking) o por plano.
- `FraiAcompanante`: Frai camina a la derecha del recorrido según la **dirección de marcha**, evita
  paredes/planos verticales con raycasts de ARCore y muestra un indicador si queda fuera de pantalla.
- `ModoSinAR`: fondo y avatar fijos si el celular no soporta ARCore.

### GPS
`GeoLocalizador` funciona en modo avión (solo satélites). Detecta la estación por `lat/lon/radio_m`;
si las coordenadas están en 0, el visitante elige la estación en el minimapa.

## Base de conocimiento y actualización

`base_conocimiento.json` contiene: `estaciones` (4), `seguridad` (7), `preguntas` frecuentes (78) y
`documentos` (207 fragmentos con guion y página). Versión actual: `1.1-guiones-uton`.

**Construir/validar:**

```bash
python Tools/construir_base.py [--version 1.1-guiones-uton]
```

Es idempotente (borra y re‑agrega los ids `g24_`, `gzn_`, `gq_`), valida ids únicos, fuentes no vacías y que
lo nuevo no sea `[PENDIENTE]`, y genera `Tools/publicar/{base_conocimiento.json, manifest.json}`.

**Actualización en el celular (`ContentUpdater`):** con señal (Manizales, hotel, entrada del parque) lee
`manifest.json` → si la versión es nueva descarga la base → verifica tamaño y **SHA‑256** → valida el JSON →
reemplaza la copia local de forma atómica. Cualquier fallo deja intacta la base actual. El QR del parque apunta
a una URL fija; lo que cambia es el contenido publicado detrás.

## Licencia de visita (GPS / tiempo)

`LicenciaVisita` bloquea la app fuera del área o tras vencer el plazo. Se configura en
`StreamingAssets/config_visita.json` **antes de compilar**:

| Campo | Valor actual | Significado |
|---|---|---|
| `lat`, `lon` | 5.0656, −75.5047 | Centro del área permitida |
| `radio_m` | 5000 | Radio permitido |
| `horas` | 24 | Duración desde que el visitante entra al área |
| `lecturas_fuera` | 3 | Lecturas GPS consecutivas fuera antes de bloquear |
| `precision_max_m` | 100 | Lecturas peores se ignoran |
| `intervalo_s` | 10 | Frecuencia de revisión |
| `dias_max_sin_activar` | 7 | Caducidad si nunca entra al área |

## Clima y LoRa

`FuenteClima` (objeto *Sistema*) tiene tres modos:

- **Simulada** (por defecto): datos de demostración; la app muestra «Datos simulados».
- **Url**: consulta cada 30 s un JSON (p. ej. `http://192.168.4.1/api/clima`).
- **Manual**: otro componente (BLE, serial…) llama a `FuenteClima.Recibir(json)`.

```json
{ "estacion": "estacion_1", "marca": 1764000000, "temperatura": 6.5, "humedad": 92,
  "presion": 622.4, "viento": 18, "lluvia": 0.4, "rssi": -97, "bateria": 81 }
```

Unidades: °C, %, hPa, km/h, mm/h, dBm, %. Los campos faltantes se omiten. Los umbrales de alerta
(Inspector de `FuenteClima`) son valores genéricos de páramo y **deben validarse con el PNN**.

## Archivos grandes (Git LFS)

- **LFS**: `Whisper/ggml-base.bin`, `LlamaLib-v2.0.5/android-arm64/native/libllamalib_android-arm64.so`.
- **No incluidos**: librerías CUDA/Vulkan de `win-x64` y `linux-x64` (reimportar LLMUnity / LlamaLib v2.0.5).
- **Modelo GGUF** (~770 MB): descargar una vez por PC.
- **Nunca se suben** (`.gitignore`): `Assets/StreamingAssets/*.gguf` y `Assets/Plugins/Android/LLMUnity/`
  (copias temporales de build).

## Estado y pendientes

- [x] Chat con RAG, seguridad, voz, AR, acompañante, visión real, licencia, actualización de contenido.
- [ ] **Hardware de clima**: ESP32 + LoRa aún no existe; la pestaña usa datos simulados.
- [ ] Validar con el PNN: umbrales de alerta, nombres/descripciones de especies, contenido `[PENDIENTE]`.
- [ ] Coordenadas GPS de las estaciones (llenar en campo si están en 0).
- [ ] Ajustar `RamMinimaMB` (6000) tras pruebas en dispositivos reales.
- [ ] Mejora futura del retriever: embeddings en lugar de BM25.
