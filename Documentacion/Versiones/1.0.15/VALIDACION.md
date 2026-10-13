# Validación 1.0.15

- Compilación Release correcta, sin errores ni advertencias.
- 39 pruebas deterministas de red: incluye desconexión/reconexión, cambios de DHCP/DNS/BSSID, reinicio del almacén de etiquetas, perfiles Ethernet diferentes, guardado sobre la red equivocada, recuperación de etiquetas antiguas y continuidad de las curvas.
- Prueba real limitada: perfil Windows de la conexión Ethernet identificado y consistente, router y ambos destinos ICMP/TCP y DNS respondieron. Una etiqueta existente se recuperó de los registros locales y quedó vinculada al perfil actual.
- Dos renderizados de interfaz a 1100×780 y 860×650 con datos de demostración: curva, pérdidas y cambios de red. Verificado que el panel no se recorta y todos sus botones tienen icono. Un problema de redimensionamiento detectado en la prueba se corrigió antes de publicar el ejecutable.
- Lucide: recursos oficiales incluidos localmente, con aviso de licencia y referencia del commit; no se descargan iconos en ejecución.
- Pruebas anteriores de versiones, enlaces y descarga del actualizador aprobadas; 147 comprobaciones de píxeles de las barras aprobadas.
- No se modificaron sensores, BIOS, conexiones, DNS ni protecciones del sistema. No se publicó esta versión en GitHub.
- Pendiente: funcionamiento prolongado en muchas redes y casos en los que Windows no proporcione un perfil estable. La identificación de fallback por SSID/interfaz/gateway tiene límites documentados.
