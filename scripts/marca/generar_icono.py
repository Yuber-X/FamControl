"""
Genera el icono de FAControl (facontrol.ico) a partir del MISMO logo vectorial
que usa la aplicacion: el monograma FA de Familia Almonte, donde la A es el
techo de una casa con ventana.

Por que existe este script y no un .ico dibujado a mano:
  - La geometria vive en `src/FAControl.Views/LogoFA.xaml`. Si el logo cambia,
    el icono se rehace corriendo esto, y no queda una version vieja pegada.
  - El .ico anterior era una "P" morada heredada de la plantilla PTV300: no
    decia nada del proyecto ni de la marca del cliente.

Uso:
    python scripts/marca/generar_icono.py

Escribe:
    src/FAControl.App/Assets/facontrol.ico     (el que compila el .exe)
    docs/imagenes/marca/facontrol-*.png        (para manuales y para Yuber)

Requiere Pillow (`pip install pillow`).
"""

from __future__ import annotations

import sys
from pathlib import Path

from PIL import Image, ImageDraw

# --------------------------------------------------------------------------
# Colores de la marca. Son los mismos de LogoFA.xaml.cs: si alla cambian, acá
# tambien — por eso van juntos y con el nombre que usa el codigo.
# --------------------------------------------------------------------------
NAVY = (0x0D, 0x1B, 0x2A, 255)      # fondo de la placa
CLARO = (0xF2, 0xF4, 0xF7, 255)     # la F
DORADO = (0xC9, 0xA1, 0x5A, 255)    # la A / techo

# Geometria de LogoFA.xaml, en su lienzo original de 140x120.
LIENZO = (140.0, 120.0)

F_TRAZO = [(10, 8), (62, 8), (62, 24), (28, 24), (28, 52),
           (56, 52), (56, 68), (28, 68), (28, 112), (10, 112)]

A_TRAZO = [(88, 8), (138, 112), (116, 112), (88, 50), (60, 112), (38, 112)]

VENTANA = (76, 72, 100, 96)          # cuadrado dorado
PANO_VERTICAL = (86.5, 72, 89.5, 96)
PANO_HORIZONTAL = (76, 82.5, 100, 85.5)

# Tamaños que Windows realmente pide: 16/20/24 en la barra de tareas y las
# listas, 32/40/48 en el escritorio, 64/128/256 en vistas grandes y en la
# ventana "Acerca de".
TAMANOS = [16, 20, 24, 32, 40, 48, 64, 128, 256]

# Por debajo de este tamaño la ventana de 4 paños se convierte en un borron de
# 1px: se dibuja el techo macizo, que a esa escala lee mejor.
SIN_VENTANA_HASTA = 24

SUPERMUESTREO = 8   # se dibuja 8x mas grande y se reduce: bordes limpios


def _placa(lado: int, radio_pct: float) -> Image.Image:
    """Cuadrado navy de esquinas redondeadas: el fondo del icono."""
    lienzo = Image.new("RGBA", (lado, lado), (0, 0, 0, 0))
    pincel = ImageDraw.Draw(lienzo)
    pincel.rounded_rectangle([0, 0, lado - 1, lado - 1],
                             radius=int(lado * radio_pct), fill=NAVY)
    return lienzo


def _dibujar_logo(pincel: ImageDraw.ImageDraw, escala: float,
                  dx: float, dy: float, con_ventana: bool) -> None:
    """Pinta el monograma FA sobre la placa, ya escalado y centrado."""
    def punto(p):
        return (dx + p[0] * escala, dy + p[1] * escala)

    def caja(c):
        return [dx + c[0] * escala, dy + c[1] * escala,
                dx + c[2] * escala, dy + c[3] * escala]

    pincel.polygon([punto(p) for p in F_TRAZO], fill=CLARO)
    pincel.polygon([punto(p) for p in A_TRAZO], fill=DORADO)

    if con_ventana:
        pincel.rectangle(caja(VENTANA), fill=DORADO)
        pincel.rectangle(caja(PANO_VERTICAL), fill=NAVY)
        pincel.rectangle(caja(PANO_HORIZONTAL), fill=NAVY)


def generar(lado: int) -> Image.Image:
    """Un fotograma del icono, en el tamaño pedido."""
    grande = lado * SUPERMUESTREO
    lienzo = _placa(grande, radio_pct=0.22)
    pincel = ImageDraw.Draw(lienzo)

    # Margen: en los tamaños chicos se aprieta el logo contra el borde para que
    # los trazos salgan mas gruesos y se distingan; en los grandes respira.
    margen = 0.16 if lado <= 32 else 0.19
    ancho_util = grande * (1 - 2 * margen)
    alto_util = grande * (1 - 2 * margen)
    escala = min(ancho_util / LIENZO[0], alto_util / LIENZO[1])

    dx = (grande - LIENZO[0] * escala) / 2
    dy = (grande - LIENZO[1] * escala) / 2

    _dibujar_logo(pincel, escala, dx, dy, con_ventana=lado > SIN_VENTANA_HASTA)

    return lienzo.resize((lado, lado), Image.LANCZOS)


def main() -> int:
    raiz = Path(__file__).resolve().parents[2]
    destino_ico = raiz / "src" / "FAControl.App" / "Assets" / "facontrol.ico"
    destino_png = raiz / "docs" / "imagenes" / "marca"
    destino_png.mkdir(parents=True, exist_ok=True)

    fotogramas = [generar(lado) for lado in TAMANOS]

    # Pillow guarda el .ico a partir de la imagen mas grande y la lista de
    # tamaños; se le pasa la de 256 con append_images para que cada tamaño
    # salga del render propio y no de un reescalado del grande.
    fotogramas[-1].save(
        destino_ico,
        format="ICO",
        sizes=[(t, t) for t in TAMANOS],
        append_images=fotogramas[:-1],
    )

    for lado, imagen in zip(TAMANOS, fotogramas):
        if lado in (48, 256):
            imagen.save(destino_png / f"facontrol-{lado}.png")

    print(f"OK  {destino_ico}  ({destino_ico.stat().st_size:,} bytes)")
    print(f"OK  {destino_png}\\facontrol-48.png y facontrol-256.png")
    return 0


if __name__ == "__main__":
    sys.exit(main())
