# Validación 1.0.14 — red

- Compilación Release correcta, sin errores ni advertencias.
- 25 pruebas deterministas del motor de red: separación por SSID/BSSID/DNS/ruta, huecos de suspensión, cambios durante prueba, etiquetas, pérdida/latencia, clasificación prudente, respuestas DNS y exportación sin registros de procesos.
- Prueba real limitada del motor en la conexión Wi-Fi actual: SSID detectado, cuatro interfaces activas identificadas, router, ambos destinos ICMP/TCP y DNS configurado respondieron. Solo confirma el intervalo probado, no estabilidad prolongada ni responsabilidad del operador.
- Las comprobaciones fueron sin acceder a sensores ni instalar controladores. Sin cambios de red, BIOS ni seguridad.
- Diseño de la ventana renderizado e inspeccionado a 1100×750 y 860×620 con datos de demostración, sin abrir el monitor real.
- Las pruebas anteriores del actualizador y las 147 comprobaciones de píxeles de barras siguen aprobadas.
- Pendiente: uso prolongado durante los fallos y cambios reales entre varias redes, VPN y conexiones IPv6-only; los cambios se validaron por simulación. No se alteró la conexión activa para forzar fallos.
- Revisión visual de la ventana principal con sensores activos no realizada en esta actualización. La función de red se ofrece sin atribuir automáticamente culpas al operador.
