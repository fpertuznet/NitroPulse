# Validación 1.0.12

- Compilación Release: correcta, sin errores ni advertencias.
- 14 pruebas de comparación y normalización de versiones, etiquetas estables y URLs de Releases.
- Descarga probada con respuestas HTTP simuladas: archivo correcto aceptado; SHA256 incorrecto, descarga incompleta, redirección ajena y versión incorrecta rechazados.
- Extracción ZIP limitada al ejecutable portable; ruta con traversal rechazada.
- Reemplazo atómico y respaldo íntegro comprobados con copias del ejecutable en una carpeta aislada. La solicitud de reinicio se interceptó para no abrir sensores.
- No se instalaron controladores ni se modificó la BIOS durante estas pruebas.
- Pendiente: prueba visual del botón y un ciclo real completo entre dos Releases públicas distintas, incluyendo cierre del monitor y reinicio. El portable se distribuye como candidato de prueba.
