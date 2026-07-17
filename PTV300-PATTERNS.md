# PTV300-PATTERNS.md — Patrones reutilizables de los proyectos de referencia

> Generado en Fase 1 (2026-07-10). Referencias clonadas desde GitHub (repos públicos de Yuber-X) en `Freelancer - Claude Code\References\`.

## Ubicación de las referencias

| Proyecto | Ruta local | Contenido |
|---|---|---|
| POS-400 (principal) | `References\POS-400\MiPOSCSharpMySQL\` | WinForms + MySQL, 6 controladores, SQL `BDPOS-400.sql` |
| PTV-300 (secundaria) | `References\PTV-300\MiPOSCSharpMySQL\` | Variante simplificada para colmados (4 controladores, sin gestión de usuarios ni caducidad) |

## Patrones REUTILIZADOS en FAControl (adaptados, no copiados)

| Patrón POS-400 | Adaptación en FAControl |
|---|---|
| `Configuracion/CConexion.cs` — conexión centralizada (credenciales hardcodeadas, MessageBox en capa de datos) | `FAControl.Data.ConexionFactory` — cadena desde `App.config`, async, sin UI en capa de datos, conexiones desechables |
| `Configuracion/SesionActual.cs` — estático con `IdUsuario`, `NombreUsuario`, `LoginTime` | `FAControl.Common.SesionActual` — agrega `SesionId` (para registrar logout) y `HaySesionActiva`; setters privados con `Iniciar()`/`Cerrar()` |
| Parámetros SQL `@param` en cada query (verificado en `ControladorVenta`) | Mismo patrón con `MySqlConnector` (`cmd.Parameters.AddWithValue`) |
| Separación `Modelos/` `Controlador/` `Configuracion/` | Evolucionada a capas: `Models / Data / Services / ViewModels / Views` con regla de dependencias unidireccional |
| Trazabilidad por `usuario_id` en `factura` (BDPOS-400.sql) | Tabla `auditoria` con `usuario_id` FK ON DELETE RESTRICT en toda mutación |
| Impresión de ticket 80mm (`FormVentas` / `PrintDocument` en `FormCuadre`) | Pendiente Fase 4: se replicará el patrón de render a imagen en `FAControl.Printing` |

## Patrones NO reutilizados (por diseño)

- ❌ `rol`, `permiso`, `rol_permiso`, `usuario_permiso` — FAControl es mono-usuario
- ❌ Contraseñas en texto plano → BCrypt cost 12 sin excepción
- ❌ `MessageBox.Show` en capa de datos → excepciones + Serilog
- ❌ Credenciales hardcodeadas → `App.config` externo
- ❌ WinForms → todo WPF/MVVM

## Diferencias PTV-300 vs POS-400 (para futuras consultas)

- PTV-300 no tiene `ControladorUsuario` ni `NotificadorCaducidad` — es la versión "simplicidad primero" que exigió aquel cliente.
- La filosofía de PTV-300 (curva de aprendizaje leve, pantalla completa, foco) es la misma que pide FAControl — útil como referencia de UX, no de código.
