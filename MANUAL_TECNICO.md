# Manual técnico — FRAI (Guía AR del Frailejón, PNN Los Nevados)

Documento para quien **mantiene, compila o amplía** la app. Para la visión general y el arranque rápido
ver [README.md](README.md). Este manual cubre el detalle: componentes, formatos de datos, parámetros,
flujos de publicación, pruebas y solución de problemas.

> Versión de la base de conocimiento al redactar: `1.1-guiones-uton` (2026-11-26).
> Unity `6000.6.10f1`, Android ARM64, namespace de código `GuiaFrailejon`.

## Contenido

1. [Alcance y requisitos](#1-alcance-y-requisitos)
2. [Arquitectura general](#2-arquitectura-general)
3. [Motor de respuestas](#3-motor-de-respuestas)
4. [Base de conocimiento](#4-base-de-conocimiento)
5. [Pipeline de contenido y actualización OTA](#5-pipeline-de-contenido-y-actualización-ota)
6. [Voz](#6-voz)
7. [Visión artificial](#7-visión-artificial)
8. [Realidad aumentada](#8-realidad-aumentada)
9. [GPS y licencia de visita](#9-gps-y-licencia-de-visita)
10. [Clima y LoRa](#10-clima-y-lora)
11. [Interfaz](#11-interfaz)
12. [Compilación y despliegue](#12-compilación-y-despliegue)
13. [Pruebas y verificación](#13-pruebas-y-verificación)
14. [Solución de problemas](#14-solución-de-problemas)
15. [Extender el sistema](#15-extender-el-sistema)
16. [Trabajo en curso y deuda técnica](#16-trabajo-en-curso-y-deuda-técnica)

---

## 1. Alcance y requisitos

**Objetivo:** guía móvil 100 % offline para la ruta de 4 estaciones (Aguacerales, Arenales, Valle Lunar,
Valle de las Tumbas). No hay backend en tiempo de ejecución; internet solo se usa, de forma opcional, para
actualizar la base de conocimiento.

| Requisito | Valor |
|---|---|
| Unity | 6000.6.10f1 (URP) |
| Plataforma objetivo | Android ARM64 (iOS parcial: solo hay puente TTS `GuiaTTS.mm`) |
| AR | AR Foundation / ARCore 6.6.2 (opcional: existe modo sin AR) |
| RAM para nivel Completo | ≥ 6000 MB (`DeviceTier.RamMinimaMB`, a calibrar) |
| Almacenamiento | APK grande: GGUF ≈ 770 MB + Whisper base + ONNX de visión |
| Herramientas | Git + **Git LFS**, Python 3 (solo para `Tools/construir_base.py`) |

## 2. Arquitectura general

```
                    ┌───────────── Objeto "Sistema" (escena GuiaFrailejon) ─────────────┐
 UI (uGUI/TMP) ───► │ GuideBrain ─► SafetyRouter / Retriever(BM25) / ILocalLLM          │
 ChatUI, GuideUI    │ KnowledgeBase ◄─ ContentUpdater (OTA)                             │
 ExplorarUI         │ WhisperSTT · ITextToSpeech · ClasificadorEspecies                  │
 ClimaUI, RecursosUI│ FuenteClima · GeoLocalizador · LicenciaVisita · DeviceTier         │
                    └────────────────────────────────────────────────────────────────────┘
 AR: FrailejonSpawner · FraiAcompanante · FrailejonAnimator · ModoSinAR · ARAvailability
```

Decisiones de diseño que el código hace cumplir:

- **Offline primero:** LLM, STT, visión y KB viajan dentro del APK.
- **Nada inventado:** toda respuesta lleva `fuente`; lo `[PENDIENTE]` nunca se lee ni se afirma.
- **La IA no redacta seguridad:** las preguntas de seguridad se resuelven antes de tocar el LLM.
- **Degradación elegante:** sin modelo, poca RAM o poca batería, la app sigue con respuestas oficiales.
- **Acoplamiento por interfaces:** `ILocalLLM`, `ISpeechToText`, `ITextToSpeech` permiten cambiar de motor.
- **La escena se genera por código** (`ArmarEscena`); no se edita a mano.

### Mapa de código (`Assets/Scripts`, ≈ 4 800 líneas)

| Carpeta | Archivos | Responsabilidad |
|---|---|---|
| `Core/` | `GuideBrain`, `Retriever`, `SafetyRouter`, `KnowledgeBase`, `TextNormalizer`, `DeviceTier`, `ContentUpdater`, `GeoLocalizador`, `LicenciaVisita`, `AvatarSeleccion` | Lógica sin UI |
| `LLM/` | `ILocalLLM`, `LLMForUnityAdapter` | Contrato y adaptador a LLMUnity |
| `Voice/` | `WhisperSTT`, `TextToSpeech` | STT local y TTS nativo |
| `Vision/` | `ClasificadorEspecies`, `CapturaCamara`, `ExplorarUI` | Clasificación de especies |
| `AR/` | `FrailejonSpawner`, `FraiAcompanante`, `FrailejonAnimator`, `ModoSinAR`, `ARAvailability` | Avatar en AR |
| `Clima/` | `FuenteClima`, `ClimaUI` | Datos y alertas meteorológicas |
| `UI/` | `GuideUI`, `ChatUI`, `NavegacionUI`, `MiniMapa`, `RecursosUI`, `SelectorAvatar`, `AreaSegura`, `BotonMantener`, `SubirConTeclado` | Interfaz construida por código |

## 3. Motor de respuestas

### 3.1 Flujo (`GuideBrain.Preguntar`)

```
pregunta ─► vacía? ─────────────────────────────────────────► SinDato
         ─► SafetyRouter.Detectar ──── coincide ────────────► Seguridad (texto fijo)
         ─► Retriever.Buscar(top 3, estación actual)
              mejor == null o puntaje < umbralMinimo ────────► SinDato
              es pregunta frecuente y puntaje ≥ umbralRespuestaDirecta ─► Oficial
         ─► Nivel Completo y LLM disponible y hay pasajes útiles
              LLM.Generar(prompt con contexto) con timeout
                ok: sin "NO_SE" y cifras respaldadas ───────► GeneradaConFuente
                "NO_SE": Oficial si es FAQ y puntaje ≥ umbralRespaldo, si no SinDato
                timeout / excepción / cifras inventadas ────► cae al paso siguiente
         ─► respaldo: puntaje ≥ umbralRespaldo ─────────────► Oficial, si no SinDato
```

### 3.2 Parámetros (Inspector de `GuideBrain`)

| Campo | Defecto | Efecto |
|---|---|---|
| `umbralRespuestaDirecta` | 0.55 | FAQ con este puntaje se responde sin LLM |
| `umbralMinimo` | 0.15 | Por debajo: «no tengo ese dato» |
| `umbralRespaldo` | 0.35 | Mínimo para dar la respuesta oficial más cercana |
| `tiempoMaxLLM` | 30 s | Pasado el tiempo se cancela el LLM |
| `forzarIAParaPruebas` | false | Ignora el límite de RAM (**solo para pruebas**) |

### 3.3 Tipos de respuesta

`TipoRespuesta`: `Seguridad`, `Oficial`, `GeneradaConFuente`, `SinDato`, `Pendiente`.
`RespuestaGuia.PuedeLeerse` es `false` solo para `Pendiente`. `Validar()` convierte cualquier texto vacío o
que empiece por `[PENDIENTE` en `Pendiente` con el mensaje `MsgPendiente`.

### 3.4 SafetyRouter

Recorre `seguridad[].palabras_clave` normalizadas. Gana la **clave más larga** («mal de altura» > «altura»).
Frases multi‑palabra: coincidencia exacta. Palabra sola de ≥ 5 letras: también por raíz (quita la última
letra, así «mareo» detecta «mareado»). Ante la duda, se prefiere entregar el protocolo de seguridad.

### 3.5 Retriever (BM25 propio)

- `k1 = 1.2`, `b = 0.75`. Cada FAQ se indexa como `preguntas + preguntas + respuesta` (las preguntas de
  ejemplo pesan doble); cada documento como `titulo + texto`.
- Filtra por estación: se descartan pasajes cuya `estacion` no sea `*` ni la actual; los de la estación actual
  reciben ×1.15.
- El puntaje se **normaliza a 0..1** dividiendo por el máximo posible. Términos que no existen en toda la base
  pesan `0.25` (`PesoDesconocida`) para que preguntas largas o con errores de voz puedan alcanzar el umbral.
- Complejidad: recorre todos los pasajes por consulta (~285 pasajes). Suficiente; si la base crece a miles,
  precalcular `tf` por pasaje.

### 3.6 TextNormalizer

`Limpiar` (minúsculas, sin tildes ni signos, espacios en los extremos) y `Tokens` (quita palabras vacías,
aplica sinónimos y raíz sencilla). **Cualquier cambio aquí cambia los puntajes de todo el sistema**: reevaluar
umbrales tras tocarlo.

### 3.7 Guardas del LLM

- Prompt fijo en `ConstruirPrompt`: máximo 3 frases, solo información oficial, responder `NO_SE` si no alcanza,
  sin consejos médicos.
- `CifrasRespaldadas`: toda cifra (`\d+([.,]\d+)?`) de la respuesta debe aparecer literal en el contexto; si no,
  se descarta.
- Texto con `NO_SE`, tiempo agotado o excepción → respuesta oficial de respaldo.
- `DeviceTier.Detectar` → `Basico` si: el usuario eligió ahorrar batería (PlayerPrefs `forzar_nivel_basico`),
  no hay modelo, RAM < 6000 MB, o batería < 20 % sin cargar. Se reevalúa en cada pregunta.

### 3.8 Adaptador LLM

`LLMForUnityAdapter` implementa `ILocalLLM` sobre `LLMAgent` de LLMUnity (`Disponible`, `Generar`,
`Cancelar`). Hay una variante «no disponible» para compilaciones sin LLMUnity. Modelo configurado en
`StreamingAssets/LLMManager.json`: `llama-3.2-1b-instruct-q4_k_m.gguf`, `contextLength` 131072,
`includeInBuild: true`, `downloadOnStart: false`.

## 4. Base de conocimiento

Archivo: `Assets/StreamingAssets/conocimiento/base_conocimiento.json` (UTF‑8). Se deserializa con
`JsonUtility` (por eso las clases son `[Serializable]` y no hay diccionarios).

| Sección | Cantidad actual | Esquema |
|---|---|---|
| `estaciones` | 4 | `id, nombre, corto, marcador, saludo, lat, lon, radio_m` |
| `seguridad` | 7 | `id, palabras_clave[], respuesta, fuente` |
| `preguntas` | 78 | `id, estacion ("*" o id), preguntas[], respuesta, fuente` |
| `documentos` | 207 | `id, titulo, texto, fuente` (siempre con guion y página) |

Además: `version`, `fecha_actualizacion`, `nota`.

**Carga** (`KnowledgeBase.Cargar`): primero la copia actualizada en `persistentDataPath/base_conocimiento.json`;
si no existe, la de `StreamingAssets` (en Android se lee con `UnityWebRequest` porque está dentro del APK). Si
el JSON es inválido queda una base vacía y se registra el error; `AlCargar` avisa a `GuideBrain`.

**Reglas de contenido:**
- Todo registro lleva `fuente` verificable. No se aceptan fuentes «equipo del proyecto» ni «pendiente de
  validación» (el script las elimina).
- `[PENDIENTE]` es válido en la base, pero **nunca** en lo que agrega el script y nunca se lee al visitante.
- Los ids de las estaciones deben coincidir con los de los marcadores AR y con el campo `estacion` de las FAQ.

> **Estaciones y GPS:** hoy `estaciones[*]` tienen coordenadas (≈ 5.0505, −75.5037) y `radio_m = 5`. Las
> cuatro quedan a pocos metros entre sí, lo que parece un valor provisional: validar en campo antes de
> depender de la detección automática de estación.

## 5. Pipeline de contenido y actualización OTA

### 5.1 Construir y validar

```bash
python Tools/construir_base.py [--version 1.1-guiones-uton]
```

1. Lee la base actual y todos los `Tools/conocimiento/*.json` (`documentos`, `preguntas`, `actualizar`).
2. **Idempotente:** borra los ids con prefijos `g24_`, `gzn_`, `gq_` y los vuelve a agregar.
3. Descarta entradas no oficiales (fuente con «equipo del proyecto» o «pendiente de validaci…»).
4. Aplica `actualizar` (correcciones por id; falla si el id no existe).
5. Valida: ids únicos, `fuente` no vacía, texto no vacío ni `[PENDIENTE`, FAQ con preguntas de ejemplo,
   `estacion` válida, existencia de `seguridad`. Cualquier error aborta con código de salida ≠ 0.
6. Escribe la base, actualiza `version`/`fecha_actualizacion`/`nota` y genera
   `Tools/publicar/base_conocimiento.json` y `Tools/publicar/manifest.json`.

Formato de una fuente en `Tools/conocimiento/*.json`:

```json
{
  "documentos": [ { "id": "g24_xxx", "titulo": "…", "texto": "…", "fuente": "Guion UTON 2024, p. 12" } ],
  "preguntas":  [ { "id": "gq_xxx", "estacion": "*", "preguntas": ["¿…?"], "respuesta": "…", "fuente": "…" } ],
  "actualizar": { "faq_que_es_frailejon": { "respuesta": "texto corregido" } }
}
```

### 5.2 Manifest

```json
{ "version": "1.1-guiones-uton", "archivo": "base_conocimiento.json", "sha256": "<hex>", "bytes": 193468 }
```

`archivo` es relativo a la URL del manifest.

### 5.3 Actualización en el dispositivo (`ContentUpdater`)

Se activa si `urlBaseConocimiento` no está vacía y hay red (`Application.internetReachability`).

1. Descarga el manifest (timeout 15 s); si es inválido, termina.
2. Si `version` ya es la instalada, termina.
3. Descarga la base (timeout 60 s).
4. Verifica tamaño (`bytes`) y **SHA‑256**.
5. Valida el JSON: debe parsear, tener `seguridad` no vacía y `version` igual a la del manifest.
6. Escribe `.tmp`, borra la copia anterior y mueve: reemplazo atómico. Recarga la KB.

Cualquier fallo deja la base actual intacta. `UsarUrl(url)` permite que un QR o *deep link* fije la URL.

**Para publicar:** correr `construir_base.py`, subir ambos archivos de `Tools/publicar/` a la misma carpeta
pública y apuntar `urlBaseConocimiento` (campo de `ContentUpdater` en el objeto *Sistema*) al `manifest.json`.
Hay que fijar la URL antes de compilar o entregarla por QR.

## 6. Voz

- **STT** (`WhisperSTT : ISpeechToText`): `whisper.unity` con `ggml-base.bin`. Grabación mientras se mantiene
  pulsado `BotonMantener`, máximo `maxSegundos` (15). Expone `Grabando`, `Disponible`, `Cargando`, `Empezar()`.
- **TTS** (`VozFactory.Instancia → ITextToSpeech`): implementaciones Android (`android.speech.tts`), iOS
  (`GuiaTTS.mm`, idioma `es-CO`) y una de simulación en Editor que solo registra en consola.
- La app solo habla respuestas con `PuedeLeerse == true`.
- El celular debe tener instalado el paquete de voz en español (Android: Ajustes › Idioma › Salida de texto a
  voz › Google › Instalar datos de voz).

## 7. Visión artificial

Se compila solo con el símbolo de scripting `GUIA_VISION` (menú *Activar visión artificial*, 3 pasos, volver a
ejecutarlo tras cada paso). Sin él, `ClasificadorEspecies` queda vacío y la pestaña Explorar lo indica.

**Archivos en `Assets/Vision/`:**

| Archivo | Contenido |
|---|---|
| `modelo_especies.onnx` | EfficientNet‑B0 (FP32/FP16), 250 especies |
| `vision.json` | Metadatos: `input` (224×224, NCHW, media/desv. ImageNet, `cropPct` 0.878431), `output.name`, `temperature`, `rejectThreshold` (0.35), `metrics`, `labels[]`, `demo` |
| `especies_info.json` | `especies[]`: `cientifico, comun, tipo, descripcion, cuidado, fuente` (pendientes de validar con el PNN) |

**Comportamiento:**
- `TipoDecision`: `Identificada`, `Incierta` (por debajo de `rejectThreshold`), `Demo` (modelo de demostración:
  **nunca** afirma una especie).
- Se identifica **solo al tomar o subir una foto**, mostrando el top‑3 con probabilidades. No hay inferencia
  continua (ahorra batería).
- Métricas del modelo actual: val top‑1 69.5 %, top‑3 84.8 %.
- **Restricción crítica:** Unity Inference Engine 2.6.1 **no importa INT8 QDQ** (`QuantizeLinear` /
  `DequantizeLinear`). Exportar siempre FP32 o FP16.
- El modelo proviene del repositorio `pnn-nevados-edge-ai` (`packages/ml-vision-pipeline`), que produce
  `vision.json` y el ONNX con este mismo formato. Para reemplazarlo: copiar ambos archivos, abrir Unity y
  comprobar que las 250 etiquetas coinciden con la salida del modelo.

## 8. Realidad aumentada

| Componente | Función |
|---|---|
| `ARAvailability` | Detecta si ARCore está disponible |
| `ModoSinAR` | Fondo (`FondoSinAR.mat`) y avatar fijos cuando no hay ARCore |
| `FrailejonSpawner` | Coloca a Frai al detectar el marcador de la estación (image tracking, `LibreriaEstaciones.asset`) o sobre un plano |
| `FraiAcompanante` | Modo «Acompáñame»: Frai camina a la derecha según la dirección de marcha, evita paredes/planos verticales con raycasts de ARCore y muestra indicador si queda fuera de pantalla |
| `FrailejonAnimator` | Animaciones del avatar |
| `AvatarSeleccion` / `SelectorAvatar` | Elección frailejón o cóndor (`FrailejonGuia` / `CondorGuia`) |

Los marcadores de `Assets/Marcadores` deben llamarse como el campo `marcador` de cada estación
(`estacion_1` … `estacion_4`). Modelos 3D `.glb` cargados con glTFast.

## 9. GPS y licencia de visita

### 9.1 GeoLocalizador
Usa el GPS (funciona en modo avión, solo satélites). Detecta la estación actual comparando con
`lat/lon/radio_m` de cada estación; si las coordenadas son 0, la estación se elige a mano en el minimapa.

### 9.2 LicenciaVisita
Se instancia sola con `RuntimeInitializeOnLoadMethod` y **no corre en el Editor**.

- **Activa** la licencia la primera vez que el GPS ve al visitante dentro del área (antes de eso puede
  preparar el viaje).
- **Bloquea** cuando: se cumplen `horas`; hay `lecturas_fuera` lecturas seguidas fuera del área; la hora del
  celular retrocede (anti‑trampa); o pasan `dias_max_sin_activar` sin haber entrado.
- Lecturas con precisión peor que `precision_max_m` se ignoran. Sin GPS (sin fix o sin permiso) **no** bloquea
  por ubicación: manda el tiempo.
- Al bloquear: borra datos descargados/guardados, desactiva la app y muestra una pantalla fija con botón de
  desinstalar. Es persistente (claves PlayerPrefs `lv_*`).

`StreamingAssets/config_visita.json` (se lee al iniciar; **editar antes de compilar**):

| Campo | Actual | Significado |
|---|---|---|
| `lat`, `lon` | 5.065556, −75.504722 | Centro del área permitida |
| `radio_m` | 5000 | Radio permitido (m) |
| `horas` | 24 | Duración desde que entra al área |
| `lecturas_fuera` | 3 | Lecturas consecutivas fuera antes de bloquear |
| `precision_max_m` | 100 | Lecturas peores se ignoran |
| `intervalo_s` | 10 | Frecuencia de revisión |
| `dias_max_sin_activar` | 7 | Caducidad si nunca activa |

> Para probar el bloqueo hay que ejecutar en dispositivo; limpiar con *Ajustes › Aplicaciones › Borrar datos*
> restablece las claves `lv_*`.

## 10. Clima y LoRa

Estado actual: **simulado** (no existe aún el hardware ESP32 + LoRa).

`FuenteClima` (objeto *Sistema*), modos:

| Modo | Comportamiento |
|---|---|
| `Simulada` (defecto) | Datos de demostración; la UI muestra «Datos simulados» |
| `Url` | Consulta cada `intervaloSegundos` (30) `url` (defecto `http://192.168.4.1/api/clima`, AP del ESP32) |
| `Manual` | Otro componente (BLE, serial…) llama `Recibir(string json)` |

**Contrato JSON** (campos ausentes se omiten; faltantes = `LecturaClima.SinDato` −999):

```json
{ "estacion": "estacion_1", "marca": 1764000000, "temperatura": 6.5, "humedad": 92,
  "presion": 622.4, "viento": 18, "lluvia": 0.4, "rssi": -97, "bateria": 81 }
```

Unidades: °C, %, hPa, km/h, mm/h, dBm, %. `marca` en segundos Unix.

**Estados:** `SinDatos`, `Conectada`, `Desactualizada` (sin lectura en `minutosVigencia` = 15), `Simulada`.

**Alertas** (`NivelAlerta`: `Info`, `Precaucion`, `Peligro`), umbrales editables en el Inspector:

| Parámetro | Valor | | Parámetro | Valor |
|---|---|---|---|---|
| `tempFrio` | 4 °C | | `lluviaModerada` | 1 mm/h |
| `tempHelada` | 0 °C | | `lluviaFuerte` | 5 mm/h |
| `vientoFuerte` | 30 km/h | | `humedadNiebla` | 95 % |
| `vientoPeligro` | 50 km/h | | `caidaPresion3h` | 2 hPa |

Los umbrales son **genéricos de páramo y deben validarse con el PNN** antes de una operación real.
`FuenteClima` expone `Ultima`, `Estado`, `Alertas`, `NivelMaximo` y el evento `AlActualizar`.

## 11. Interfaz

Toda la UI se crea por código (`ArmarEscena`, `EstiloUI`); los sprites de `Assets/Materiales/UI` se regeneran
borrando esa carpeta y rearmando la escena. Pestañas: **Guía**, **Explorar**, **Clima**, **Recursos**
(`NavegacionUI`). `AreaSegura` respeta recortes/notch; `SubirConTeclado` sube el chat al abrir el teclado;
`RecursosUI` (FPS, CPU, memoria, batería) solo mide mientras es visible, para no gastar batería.

## 12. Compilación y despliegue

### 12.1 Instalación en un PC nuevo
1. `git lfs install` y `git clone`.
2. Abrir con Unity 6000.6.10f1; esperar la importación.
3. Menú **Guia Frailejon › Armar escena completa**.
4. Objeto **Sistema › LLM › Download model › Llama 3.2 1B Instruct (Q4_K_M)** (~770 MB).
5. Repetir **Armar escena completa** (asigna el modelo: `[ActivarIA] Modelo asignado: …`).
6. Compilar con el perfil de `Assets/Settings/Build Profiles` (Android, ARM64).

### 12.2 Menús del editor (*Guia Frailejon >*)

| Menú | Script | Acción |
|---|---|---|
| Armar escena completa | `ArmarEscena` | Regenera `GuiaFrailejon.unity` (XR Origin, UI, sistema, íconos) |
| Activar IA (nivel completo) | `ActivarIA` | Conecta el GGUF al componente LLM |
| Activar voz | `ActivarVoz` | Conecta Whisper |
| Activar visión artificial | `ActivarVision` | Instala Inference Engine, agrega `GUIA_VISION`, conecta el modelo |

### 12.3 Lista de verificación antes de compilar
- [ ] `python Tools/construir_base.py` sin errores y base copiada.
- [ ] `config_visita.json` con área y horas correctas.
- [ ] `ContentUpdater.urlBaseConocimiento` apunta al manifest publicado (o vacío a propósito).
- [ ] Modelo GGUF presente y asignado; `LLMManager.json` con `includeInBuild: true`.
- [ ] `GUIA_VISION` activo y `vision.json` con `demo: false`.
- [ ] Archivos LFS descargados (`ggml-base.bin`, `libllamalib_android-arm64.so`).
- [ ] Coordenadas de estaciones validadas en campo.
- [ ] `FuenteClima.modo` correcto (Simulada si no hay hardware).

### 12.4 Git LFS y archivos excluidos
- **LFS:** `Whisper/ggml-base.bin`, `LlamaLib-v2.0.5/android-arm64/native/libllamalib_android-arm64.so`.
- **No incluidos:** librerías CUDA/Vulkan de `win-x64` y `linux-x64` (reimportar LLMUnity / LlamaLib v2.0.5).
- **Nunca se suben (`.gitignore`):** `Assets/StreamingAssets/*.gguf`, `Assets/Plugins/Android/LLMUnity/`.
- `Demo.apk` y carpetas `Library/`, `Temp/`, `Logs/`, `build/`, `LLMUnityBuild/` son locales/generadas.

## 13. Pruebas y verificación

El proyecto no tiene suite automatizada; la verificación es manual. Casos mínimos:

| Área | Caso | Resultado esperado |
|---|---|---|
| Seguridad | «me siento mareado» | Texto fijo de mal de altura (`Seguridad`) |
| FAQ | «qué es un frailejón» | Respuesta oficial directa, con fuente |
| Fuera de tema | «cuál es la capital de Francia» | `MsgSinDato` |
| Pendiente | Pregunta cuya respuesta empieza por `[PENDIENTE` | `MsgPendiente`, no se lee en voz alta |
| Nivel básico | Dispositivo < 6 GB o «ahorrar batería» | Solo respuestas oficiales, sin LLM |
| LLM | Cifra inventada por el modelo | Se descarta y se usa la oficial |
| OTA | Manifest con hash incorrecto | «hash no coincide, descartado», base intacta |
| OTA | Versión igual a la instalada | No descarga |
| Visión | Foto fuera de las 250 especies | `Incierta` (< 0.35) |
| Licencia | Fuera del área 3 lecturas seguidas | Pantalla de bloqueo persistente |
| Clima | Modo Simulada | Etiqueta «Datos simulados» |

Los registros útiles se etiquetan: `[KB]`, `[Guia]`, `[Update]`, `[ActivarIA]`, `[Voz]`.
Para pruebas de LLM en un equipo con poca RAM activar `forzarIAParaPruebas` (y desactivarlo antes de entregar).

## 14. Solución de problemas

| Síntoma | Causa probable | Solución |
|---|---|---|
| Frai solo da respuestas literales | Sin modelo, RAM < 6 GB o batería baja | Verificar `[Guia] Nivel:` en el log; descargar el GGUF; revisar `DeviceTier` |
| «No tengo ese dato» para preguntas válidas | Puntaje < umbral | Añadir frases de ejemplo a la FAQ; revisar `TextNormalizer`; bajar `umbralMinimo` con cuidado |
| El modelo ONNX no carga | Exportado INT8 QDQ | Reexportar en FP32/FP16 |
| La pestaña Explorar dice que no hay visión | Falta `GUIA_VISION` o el paquete | Ejecutar *Activar visión artificial* tres veces |
| Frai no habla | Falta paquete de voz español | Instalarlo en los ajustes de TTS de Android |
| El micrófono no transcribe | Permiso denegado o `ggml-base.bin` no descargado por LFS | Conceder permiso; `git lfs pull` |
| Build falla por librería nativa | LlamaLib sin LFS o sin reimportar | `git lfs pull`; reimportar LlamaLib v2.0.5 |
| App bloqueada en pruebas | `LicenciaVisita` activó el bloqueo | Borrar datos de la app; ajustar `config_visita.json` |
| Base no se actualiza | URL vacía, sin red, versión igual o hash incorrecto | Ver mensajes `[Update]` |
| El script de base aborta | Id repetido, fuente vacía o `[PENDIENTE` en contenido nuevo | Leer la lista de `ERRORES:` |
| Caracteres raros en la base (`�`) | Archivo guardado sin UTF‑8 | Guardar en UTF‑8 (el script lee `utf-8-sig`) |
| Frai atraviesa paredes en modo acompañante | Planos no detectados por ARCore | Mover el celular para escanear el entorno |

## 15. Extender el sistema

- **Agregar contenido:** crear/editar un JSON en `Tools/conocimiento/` con ids de prefijo `g24_`, `gzn_` o
  `gq_` (los únicos que el script regenera), correr `construir_base.py`, publicar.
  Con otro prefijo habría que añadirlo a `PREFIJOS` para mantener la idempotencia.
- **Agregar una estación:** nueva entrada en `estaciones`, imagen en `Marcadores/` registrada en
  `LibreriaEstaciones.asset`, preguntas con su `estacion`, y regenerar la escena.
- **Cambiar de LLM:** implementar `ILocalLLM` y arrastrar el componente a `GuideBrain.llmComponente`.
- **Cambiar el buscador:** sustituir `Retriever` manteniendo la firma
  `Buscar(pregunta, estacionId, top) → List<Resultado>` con puntaje 0..1 (los umbrales dependen de esa escala).
- **Nueva fuente de clima:** llamar `FuenteClima.Recibir(json)` con el contrato de la sección 10.
- **Nuevo avatar:** prefab en `Assets/Prefabs/` y registrarlo en `AvatarSeleccion`.

## 16. Trabajo en curso y deuda técnica

**Pendientes con el PNN/UTON:** validar umbrales de alerta de clima, nombres y descripciones de especies, y el
contenido `[PENDIENTE]`; confirmar las coordenadas reales de cada estación.

**Pendientes técnicos:**
- Hardware ESP32 + LoRa inexistente: la pestaña Clima usa datos simulados.
- Calibrar `RamMinimaMB` (6000) con pruebas en dispositivos reales.
- Sustituir BM25 por **embeddings** (mejora futura ya prevista en `Retriever`).
- No hay pruebas automatizadas (candidatos: tests de EditMode para `Retriever`, `SafetyRouter`,
  `CifrasRespaldadas` y `ContentUpdater`).
- iOS: solo existe el puente TTS; el resto no se ha validado.

**Trabajo sin versionar detectado (`git status`):** las carpetas `Tools/embeddings/` (contiene `cache/` con
`model.onnx` y `tokenizer.json`) y `service/app/` (solo `__pycache__`) aún no están en el repositorio ni
documentadas; parecen el inicio del reemplazo de BM25 por embeddings y de un servicio auxiliar. Cuando tengan
código, documentar aquí su propósito, interfaz y cómo se integran con `Retriever`/`ContentUpdater`.
