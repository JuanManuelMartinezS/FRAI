# FRAI

Proyecto Unity (AR / Android) del Frailejón con LLM local (LLMUnity) y reconocimiento de voz (Whisper).

## Archivos grandes

- **Git LFS** (se descargan solos con `git lfs install` + `git clone`):
  - `Assets/StreamingAssets/Whisper/ggml-base.bin`
  - `Assets/StreamingAssets/LlamaLib-v2.0.5/android-arm64/native/libllamalib_android-arm64.so`
- **No incluidos** (ignorados por tamaño; solo para PC): las librerías CUDA/tinyblas/Vulkan de
  `LlamaLib-v2.0.5/win-x64` y `linux-x64`. Se regeneran reimportando el paquete LLMUnity / LlamaLib v2.0.5.
- **APK de demo**: descárgalo desde la sección *Releases* del repositorio.

## Interfaz

La escena se genera por código: menú **Guia Frailejon > Armar escena completa** (reemplaza
`Assets/Scenes/GuiaFrailejon.unity`). La interfaz tiene tres pestañas en la barra inferior:

| Pestaña | Qué hace |
|---|---|
| **Guía** | Estación actual, recorrido (1–4), chat con Frai (texto y voz), botones Silenciar / Acompáñame / Ven aquí, SOS |
| **Explorar** | Visión artificial: identifica especies con la cámara, 100 % en el celular |
| **Clima** | Lectura de la estación meteorológica (ESP32 + LoRa), alertas y recomendaciones |

Los íconos y sprites (esquinas redondeadas, sombras, degradados) se generan en `Assets/Materiales/UI`.
Para regenerarlos, borren esa carpeta y vuelvan a armar la escena.

## Visión artificial

1. Menú **Guia Frailejon > Activar visión artificial** (3 pasos: instala Unity Inference Engine, agrega el
   símbolo `GUIA_VISION` y conecta el modelo). Ejecútenlo de nuevo después de cada paso.
2. El modelo va en `Assets/Vision/`: un `.onnx` + `vision.json` (mismo formato que genera
   `packages/ml-vision-pipeline`). El que viene incluido es el de **demostración** (pesos aleatorios):
   la app lo avisa y nunca afirma una especie.
3. Para usar el modelo entrenado, copien el ONNX **FP32 o FP16** y su `vision.json` en `Assets/Vision/`
   y ejecuten el menú otra vez. El ONNX INT8 (QDQ) no sirve: Inference Engine no importa
   `QuantizeLinear/DequantizeLinear`.
4. Nombres comunes y descripciones: `Assets/Vision/especies_info.json` (pendientes de validar con el PNN).

## Clima y LoRa

`FuenteClima` (objeto *Sistema*) tiene tres modos:

- **Simulada** (por defecto): datos de demostración; la app muestra «Datos simulados».
- **Url**: consulta cada 30 s un JSON, p. ej. el servidor de la estación (`http://192.168.4.1/api/clima`).
- **Manual**: otro componente (BLE, serial…) llama a `FuenteClima.Recibir(json)`.

Formato de una lectura (los campos que falten se omiten):

```json
{ "estacion": "estacion_1", "marca": 1764000000, "temperatura": 6.5, "humedad": 92,
  "presion": 622.4, "viento": 18, "lluvia": 0.4, "rssi": -97, "bateria": 81 }
```

Unidades: °C, %, hPa, km/h, mm/h, dBm, %. Los umbrales de alerta están en el Inspector de
`FuenteClima` y deben validarse con el PNN.
