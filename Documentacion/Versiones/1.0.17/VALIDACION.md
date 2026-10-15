# Validación local de NitroPulse 1.0.17

Fecha: 15 de octubre de 2026. Proyecto: `C:\Users\info\Documents\NitroPulse`.

## Cambios

- Navegación horizontal en las gráficas de Red, Uso de recursos y Temperaturas: barra de desplazamiento, arrastre, rueda, flechas, Inicio y Fin; botón **En vivo** con icono Lucide.
- La ventana histórica queda anclada por la fecha de su primera muestra, aunque sigan llegando mediciones. Al agotar la retención, se limita a la primera muestra disponible sin salir de rango.
- Retención acotada a 1.440 muestras de red y 3.600 de recursos: aproximadamente dos horas a la frecuencia habitual. Ventanas de 360 y 300 muestras, respectivamente. No importa CSV antiguos ni recupera otras sesiones.
- Estadísticas y marcas de red corresponden a la ventana visible. Se conservan discontinuidades por pausas, cambios y pings sin respuesta. En Recursos tampoco se unen muestras separadas por más de seis segundos.
- CPU, GPU y RAM sustituyen su barra por una exclamación de su mismo color al llegar a un valor finito de 100% o superior. Al bajar, recuperan la barra. Los indicadores de temperatura no cambian.
- Caché de iconos por alturas de píxel y estado de alerta: permite las transiciones 99,6 → 100 → 99,6 sin dejar una barra o exclamación obsoleta. El tooltip ya no redondea porcentajes inferiores a 100 como si fueran 100.

## Verificación

- Antes del cambio: 147 comprobaciones de barras y renderizado de Red a 1100×780 y 860×650 correctos.
- Después del cambio: 147 comprobaciones de barras por debajo de 100% y 63 comprobaciones de exclamaciones (tres colores, siete tamaños y tres valores saturados). Pruebas adicionales para datos desconocidos, NaN, infinito, caché y tooltip.
- Navegación determinista: inicio en vivo, desplazamiento, anclaje al recibir datos nuevos, recorte de retención, ambos límites, regreso a vivo y conjunto vacío.
- Interfaz con 960 muestras sintéticas de red, sin iniciar pruebas de conexión: barra, arrastre, rueda, Inicio/Fin, muestra entrante sin mover el historial y vuelta al final. Capturas de vista en vivo e histórica, recursos y temperaturas; inspección visual a 860 píxeles.
- Exportación PNG y etiquetas, botones con iconos Lucide y redimensionamiento sin recortes correctos.
- 39 pruebas de red aprobadas. Pruebas de versiones/enlaces, descarga segura, caché/esperas del actualizador y avisos/licencias aprobadas.
- Compilación y publicación autocontenida Windows x64 correctas, sin advertencias ni errores. Versión interna del ejecutable: `1.0.17.0`.
- Ejecutable: `Aplicacion/Versiones/1.0.17/NitroPulse.exe`.
- SHA256: `921C7D8AC363956B92768F56C0DEE41B8D02B91EB5D21348F8B3AC8980791487`.

## Alcance y pendientes

Las capturas usan datos de demostración; no son diagnósticos de una red real. No se inició la aplicación completa ni se sustituyó la versión que el usuario pudiera estar ejecutando. Falta comprobar la percepción de los iconos en la bandeja real con el escalado de Windows del usuario y el uso prolongado del desplazamiento durante una sesión real.

Se conservaron los registros personales y las versiones anteriores. No se modificaron BIOS, sensores, controladores, conexiones ni inicio automático. Esta validación se realizó antes de publicar la Release; no se ejecutó una actualización automática. Consulta Releases para comprobar la publicación y el SHA256 del archivo distribuido.
