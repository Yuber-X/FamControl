# Recordatorios por WhatsApp — por qué queda para después

> Decisión con Yuber (2026-07-19): por ahora los recordatorios van por **Gmail**
> (ya implementado). WhatsApp queda como fase aparte. Este doc explica por qué.

## El problema

No se puede "mandar WhatsApp desde la app" gratis y automático. WhatsApp NO
permite que un programa envíe mensajes libremente: hay que pasar por su API
oficial, que tiene requisitos y costos.

## Opciones reales

### 1. WhatsApp Business Platform (API oficial de Meta)
- Requiere una cuenta de WhatsApp Business + un número dedicado verificado.
- Los mensajes iniciados por el negocio deben usar **plantillas aprobadas
  por Meta** (no se puede mandar texto libre a alguien que no escribió primero).
- Se paga **por conversación**. Hay un tramo gratis mensual, luego tarifa por país.
- Setup: registrar la app en Meta, verificar el número, esperar aprobación de
  plantillas. Días de trámite, no minutos.

### 2. Proveedor intermediario (Twilio, 360dialog, etc.)
- Más fácil de integrar que Meta directo, pero **de pago** (mensualidad + por mensaje).
- Igual usa la API oficial por debajo, así que aplican las plantillas aprobadas.

### 3. Automatizar WhatsApp Web (NO recomendado)
- Librerías no oficiales que controlan WhatsApp Web. **Violan los términos de
  servicio de WhatsApp** y arriesgan que **baneen el número del cliente**.
- Frágiles: se rompen cada vez que WhatsApp cambia algo.
- **No se hace en un sistema que el cliente usa para su negocio.**

## Alternativa de bajo costo, sin API (opción intermedia)

Si el cliente no quiere pagar la API pero quiere la comodidad, se puede agregar
un **botón "Enviar por WhatsApp"** por cliente que **abre WhatsApp** (app o web)
con el mensaje ya escrito, usando `https://wa.me/<número>?text=<mensaje>`. El
usuario solo pulsa enviar. No es automático ni masivo, pero:
- No cuesta nada ni requiere API.
- No arriesga el número.
- Sirve para el caso "quiero avisarle a este cliente ahora".

Esto es implementable en poco tiempo si el cliente lo pide.

## Recomendación

- **Ahora:** recordatorios por Gmail (hecho).
- **Si el cliente quiere WhatsApp cómodo y gratis:** botón `wa.me` (un clic por cliente).
- **Si quiere WhatsApp automático/masivo:** presupuestar la API oficial o Twilio;
  es trabajo con costo recurrente y trámite de aprobación.
