# Respaldo en la nube — Google Drive / OneDrive

> Estado: **la opción recomendada ya funciona hoy**. La integración directa por
> API queda para un tier posterior (ver §2). Pedido del cliente 2026-07-19.

## 1. Opción recomendada (ya implementada, sin código de nube)

El respaldo automático de FAControl escribe el `.sql` en la **carpeta que el
usuario elija** (Configuración → Respaldo de la base de datos → "Respaldar
automáticamente").

Tanto **OneDrive** como **Google Drive** instalan en Windows una carpeta local
que sincronizan con la nube en segundo plano:

- OneDrive: `C:\Users\<usuario>\OneDrive`
- Google Drive: una unidad tipo `G:\Mi unidad` (con Google Drive para escritorio)

**Si el usuario apunta la carpeta de respaldo a una subcarpeta de esas, cada
respaldo se sube solo a la nube**, sin que FAControl tenga que hablar con
ninguna API. Es la solución más simple y robusta para un negocio pequeño:

- No requiere registrar la app en Google Cloud / Azure.
- No requiere que el usuario haga login OAuth dentro de FAControl.
- No hay tokens que caduquen ni credenciales que mantener.
- Funciona igual con Dropbox, Mega, etc.

La app ya lo sugiere con un consejo en pantalla, junto al selector de carpeta.

**Pasos para el cliente:**
1. Instalar OneDrive o Google Drive para escritorio (viene con Windows / se baja gratis).
2. En FAControl → Configuración → activar "Respaldar automáticamente".
3. En "Elegir…", seleccionar una carpeta dentro de OneDrive/Google Drive
   (ej. `OneDrive\FAControl-Respaldos`).
4. Listo: cada N días se genera el respaldo y la nube lo sube solo.

## 2. Integración directa por API (diferida)

Subir el `.sql` directamente desde FAControl a Drive/OneDrive vía su API es
posible pero **tiene un costo de setup que no se justifica para el martes**:

| Requisito | Google Drive | OneDrive |
|---|---|---|
| Registrar la app | Google Cloud Console + pantalla de consentimiento OAuth | Azure App Registration |
| Credenciales | client_id + client_secret embebidos en la app | client_id + secret |
| Login del usuario | flujo OAuth "installed app" (redirect a localhost) | idem MSAL |
| Tokens | guardar y refrescar el refresh_token por PC | idem |
| NuGet | `Google.Apis.Drive.v3` | `Microsoft.Graph` + `Microsoft.Identity.Client` |
| Verificación de Google | apps que piden scope de Drive pueden requerir revisión de Google | — |

Riesgos: la pantalla de consentimiento sin verificar muestra advertencias de
"app no verificada"; los tokens caducan y hay que manejar el refresco; si el
secret se filtra (está en un exe distribuido) hay que rotarlo.

**Conclusión:** la opción 1 cubre la necesidad real (respaldo fuera del equipo)
sin nada de esto. Si el cliente insiste en la subida directa desde la app, se
agenda como trabajo aparte con su presupuesto, después de la presentación.
