#!/usr/bin/env python3
"""Fusiona Tools/conocimiento/*.json en la base de conocimiento, la valida y genera el manifest.

Uso:  python Tools/construir_base.py [--version 1.1-guiones-uton]

- Lee Assets/StreamingAssets/conocimiento/base_conocimiento.json
- Quita lo que este script agrego antes (ids g24_, gzn_, gq_) y vuelve a agregar: es idempotente.
- Aplica 'actualizar' (correcciones sobre entradas existentes por id).
- Valida: ids unicos, textos y fuentes no vacios, nada [PENDIENTE] en lo nuevo, seguridad presente.
- Escribe la base y, para publicar, Tools/publicar/{base_conocimiento.json, manifest.json}
  (manifest = lo que lee ContentUpdater: version, archivo, sha256, bytes).
"""
import argparse, glob, hashlib, json, os, sys
from datetime import date

RAIZ = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BASE = os.path.join(RAIZ, "Assets", "StreamingAssets", "conocimiento", "base_conocimiento.json")
FUENTES = os.path.join(RAIZ, "Tools", "conocimiento")
SALIDA = os.path.join(RAIZ, "Tools", "publicar")
PREFIJOS = ("g24_", "gzn_", "gq_")
NOTA_GUIONES = (" Se agregaron fragmentos del Guion Ecoturístico UTON 2024 y del Guion turístico zona norte, "
                "con guion y página en cada fuente.")


def cargar(ruta):
    with open(ruta, encoding="utf-8-sig") as f:
        return json.load(f)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--version", default="1.1-guiones-uton")
    args = ap.parse_args()

    base = cargar(BASE)
    nuevos_doc, nuevas_preg, cambios = [], [], {}
    for ruta in sorted(glob.glob(os.path.join(FUENTES, "*.json"))):
        d = cargar(ruta)
        nuevos_doc += d.get("documentos", [])
        nuevas_preg += d.get("preguntas", [])
        cambios.update(d.get("actualizar", {}))

    base["documentos"] = [x for x in base["documentos"] if not x["id"].startswith(PREFIJOS)] + nuevos_doc
    base["preguntas"] = [x for x in base["preguntas"] if not x["id"].startswith(PREFIJOS)] + nuevas_preg

    # Solo contenido oficial: se descarta lo que cite 'descripcion del equipo' o este pendiente de validar.
    def no_oficial(x):
        f = x.get("fuente", "").lower()
        return "equipo del proyecto" in f or "pendiente de validaci" in f
    for clave in ("preguntas", "documentos", "seguridad"):
        quitados = [x["id"] for x in base[clave] if no_oficial(x)]
        base[clave] = [x for x in base[clave] if not no_oficial(x)]
        if quitados: print(f"Quitado de {clave} (no oficial): {', '.join(quitados)}")

    por_id = {x["id"]: x for x in base["preguntas"] + base["documentos"]}
    for id_, campos in cambios.items():
        if id_ not in por_id:
            sys.exit(f"ERROR: 'actualizar' apunta a un id que no existe: {id_}")
        por_id[id_].update(campos)

    errores = []
    ids = [x["id"] for x in base["preguntas"] + base["documentos"] + base["seguridad"]]
    for i in {i for i in ids if ids.count(i) > 1}:
        errores.append(f"id repetido: {i}")
    ids_estacion = {e["id"] for e in base["estaciones"]} | {"*"}
    for x in nuevos_doc + nuevas_preg:
        if not x.get("fuente", "").strip(): errores.append(f"{x['id']}: sin fuente")
        txt = x.get("texto") or x.get("respuesta") or ""
        if not txt.strip() or txt.lstrip().startswith("[PENDIENTE"): errores.append(f"{x['id']}: texto vacio o pendiente")
    for p in nuevas_preg:
        if not p.get("preguntas"): errores.append(f"{p['id']}: sin preguntas de ejemplo")
        if p.get("estacion") not in ids_estacion: errores.append(f"{p['id']}: estacion invalida {p.get('estacion')}")
    if not base["seguridad"]: errores.append("la base no tiene seguridad")
    if errores:
        sys.exit("ERRORES:\n  " + "\n  ".join(errores))

    base["version"] = args.version
    base["fecha_actualizacion"] = date.today().isoformat()
    if NOTA_GUIONES.strip() not in base.get("nota", ""):
        base["nota"] = base.get("nota", "") + NOTA_GUIONES

    datos = (json.dumps(base, ensure_ascii=False, indent=2) + "\n").encode("utf-8")
    with open(BASE, "wb") as f: f.write(datos)

    os.makedirs(SALIDA, exist_ok=True)
    with open(os.path.join(SALIDA, "base_conocimiento.json"), "wb") as f: f.write(datos)
    manifest = {"version": base["version"], "archivo": "base_conocimiento.json",
                "sha256": hashlib.sha256(datos).hexdigest(), "bytes": len(datos)}
    with open(os.path.join(SALIDA, "manifest.json"), "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=2)

    print(f"OK  v{base['version']}  documentos={len(base['documentos'])}  preguntas={len(base['preguntas'])}  "
          f"seguridad={len(base['seguridad'])}  ({len(nuevos_doc)} docs + {len(nuevas_preg)} preguntas de los guiones)")
    print(f"manifest: {manifest}")


if __name__ == "__main__":
    main()
