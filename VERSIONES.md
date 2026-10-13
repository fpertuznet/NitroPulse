# Versiones locales de NitroPulse

Organizadas el 11 de octubre de 2026 en `C:\Users\info\Documents\NitroPulse`.

La versión más reciente disponible aquí es **1.0.16** (solo local):
`Aplicacion\Versiones\1.0.16\NitroPulse.exe`.
Antes de abrirla, cierra cualquier otra versión desde **Salir** en su menú junto al reloj.

Los archivos que estaban sueltos en `Aplicacion` se trasladaron a `Aplicacion\Versiones\1.0.14`, comprobando que coincidían con el respaldo existente. Ya no hay ejecutables sueltos en `Aplicacion`. No se instaló ninguna actualización ni se modificó el inicio automático de Windows; cualquier acceso directo a la ruta anterior debe apuntar ahora al ejecutable de la versión elegida.

## Carpetas

```text
NitroPulse/
  Aplicacion/
    Versiones/
      1.0.5/Compilacion-tecnica/    Respaldo antiguo, NO portable
      1.0.9/                       Ejecutable y ZIP original
      1.0.10/                      Ejecutable y ZIP original
      1.0.11/                      Ejecutable y ZIP original
      1.0.12/                      Ejecutable y ZIP original
      1.0.13/                      Ejecutable
      1.0.14/                      Ejecutable y símbolos, sin duplicados sueltos
      1.0.15/                      Ejecutable anterior
      1.0.16/                      Privacidad, licencias y espera de API; solo local
      SHA256SUMS.txt               Huellas de ejecutables y biblioteca antigua
  Documentacion/
    Versiones/
      1.0.9/VALIDACION.md
      1.0.10/VALIDACION.md
      1.0.11/VALIDACION.md
      1.0.12/VALIDACION.md
      1.0.14/VALIDACION.md
      1.0.15/VALIDACION.md
  Fuente/                          Código actual 1.0.16
  Tests/                           Pruebas de recursos, red, avisos y actualizaciones
  Iconos/                          Imágenes e icono del proyecto
  Licencias/                       Licencias y avisos de terceros
  Diagnosticos/                    Informes privados, excluidos de Git
```

Cada carpeta de versión portable contiene `NitroPulse.exe`; los símbolos de depuración disponibles se conservan como `NitroPulse.pdb`. Los ZIP se encuentran dentro de su subcarpeta `Paquetes`. No se crearon paquetes nuevos ni se reconstruyeron versiones antiguas.

La copia técnica antigua presenta `FileVersion 1.0.5.0`. Se conservaron sus 54 archivos juntos, incluidas bibliotecas y runtimes: **no es un ejecutable portable independiente**. Solo se archivó, sin comprobar su funcionamiento ni ejecutarla.

## Disponibilidad

| Versión | Ejecutable | ZIP original | Informe de validación |
| --- | --- | --- | --- |
| 1.0.5 (técnica) | [Respaldo, no portable](Aplicacion/Versiones/1.0.5/Compilacion-tecnica/) | No encontrado | Sin informe individual encontrado |
| 1.0.9 | [Abrir carpeta](Aplicacion/Versiones/1.0.9/) | [Portable](Aplicacion/Versiones/1.0.9/Paquetes/NitroPulse-1.0.9-portable.zip) | [Validación](Documentacion/Versiones/1.0.9/VALIDACION.md) |
| 1.0.10 | [Abrir carpeta](Aplicacion/Versiones/1.0.10/) | [Icono](Aplicacion/Versiones/1.0.10/Paquetes/NitroPulse-1.0.10-icono.zip) | [Validación](Documentacion/Versiones/1.0.10/VALIDACION.md) |
| 1.0.11 | [Abrir carpeta](Aplicacion/Versiones/1.0.11/) | [Actualizaciones](Aplicacion/Versiones/1.0.11/Paquetes/NitroPulse-1.0.11-actualizaciones.zip) | [Validación](Documentacion/Versiones/1.0.11/VALIDACION.md) |
| 1.0.12 | [Abrir carpeta](Aplicacion/Versiones/1.0.12/) | [Portable](Aplicacion/Versiones/1.0.12/Paquetes/NitroPulse-1.0.12-portable.zip) | [Validación](Documentacion/Versiones/1.0.12/VALIDACION.md) |
| 1.0.13 | [Abrir carpeta](Aplicacion/Versiones/1.0.13/) | No encontrado | Sin informe individual encontrado |
| 1.0.14 | [Abrir carpeta](Aplicacion/Versiones/1.0.14/) | No encontrado | [Validación](Documentacion/Versiones/1.0.14/VALIDACION.md) |
| 1.0.15 | [Abrir carpeta](Aplicacion/Versiones/1.0.15/) | No encontrado | [Validación](Documentacion/Versiones/1.0.15/VALIDACION.md) |

Este inventario refleja únicamente los archivos locales encontrados. No acredita una publicación en GitHub. Los paquetes 1.0.9, 1.0.10 y 1.0.11 conservan sus fuentes históricas dentro del ZIP; no se mezclaron con `Fuente/`. No se encontraron aquí paquetes anteriores a 1.0.9.

Las copias originales de `outputs` y `work` del chat se conservaron para no romper enlaces anteriores. No se incorporaron al proyecto capturas de la apelación de GitHub ni otros documentos personales.

## Verificación y recuperación

- Verificadas las versiones internas de los siete ejecutables y sus huellas SHA256.
- Los 54 archivos de la copia técnica antigua coinciden por SHA256 con sus originales; su ejecutable y biblioteca principal también figuran en el inventario de huellas.
- Los cuatro ZIP copiados coinciden con sus originales; el ejecutable dentro de cada ZIP coincide con el de su carpeta.
- Los informes movidos conservaron su contenido; el antiguo `VALIDACION.md` de la raíz corresponde ahora a `Documentacion/Versiones/1.0.10/VALIDACION.md`. La raíz contiene un índice nuevo.
- Los antiguos `Aplicacion/NitroPulse-1.0.12.anterior.exe` y `Aplicacion/NitroPulse-1.0.13+69de6c90fea24595605c78dd874e4a56b47abcc9.anterior.exe` se movieron a sus respectivas carpetas como `NitroPulse.exe`, sin alterar sus bytes.
- Para recuperar una copia anterior, cierra NitroPulse y usa el ejecutable de su carpeta. No sobrescribas una aplicación abierta. Los programas antiguos pueden no comprender las funciones o los formatos nuevos.
- Para deshacer la organización de los informes, devuelve cada `VALIDACION.md` a la raíz con su nombre original `VALIDACION-<versión>.md`; el de 1.0.10 tenía el nombre `VALIDACION.md`. Conserva primero este índice si quieres restaurar ese nombre.

No se borraron versiones ni se modificaron código, sensores, controladores, BIOS, red, configuración personal o registros de AppData. Los ejecutables y los ZIP siguen excluidos de Git mediante `.gitignore`.
