# Changelog — FAControl

Formato: [Keep a Changelog](https://keepachangelog.com/es/1.0.0/). Fechas en hora de República Dominicana.

## [1.0.1] — 2026-07-11 · Arranque robusto sin base de datos + auto-aprovisionamiento

### Fixed
- **La app crasheaba al instante en una PC con MySQL pero sin la base de datos creada** (excepción sin capturar en el `Loaded` del login). Ahora el arranque diagnostica la conexión ANTES de mostrar ventanas y responde con mensajes claros: servicio MySQL detenido, credenciales rechazadas o base de datos inexistente.

### Added
- **Auto-aprovisionamiento del primer arranque**: si el servidor responde pero la BD no existe, la app ofrece crearla con un clic (schema completo embebido en el ensamblado — misma fuente que `scripts/db/001_create_schema.sql`). Verificado end-to-end: crear BD desde cero → wizard de cuenta → sesión operativa. Si el usuario MySQL configurado no tiene permiso CREATE (el dedicado `facontrol`, por diseño), el mensaje dirige al INSTALL.md.
- `VerificadorBaseDatos` (Data) con diagnóstico `Lista/FaltaBaseDatos/CredencialesInvalidas/SinServidor` + 5 tests de integración nuevos (76 tests en total).

### Seguridad
- `App.config` versionado ahora lleva placeholders (`CAMBIAR_USUARIO`/`CAMBIAR_PASSWORD`): las credenciales locales de Dev ya no viven en el repositorio público (protegidas además con `git update-index --skip-worktree`).

## [1.0.0] — 2026-07-10 · Fase 7 (Empaquetado y entrega) — TODAS LAS FASES COMPLETAS

### Added
- **Instalador** `FAControl_Setup_1.0.0.exe` (Inno Setup 6, español, 60 MB): app publicada **self-contained win-x64** (el cliente NO necesita instalar .NET), acceso directo, scripts de BD y documentación incluidos. El App.config no se pisa en actualizaciones; permisos de escritura para logs/ajustes.
- **`scripts/db/003_crear_usuario_dedicado.sql`**: usuario MySQL `facontrol` con permisos mínimos (sin DELETE/DROP — la app usa soft deletes) para no correr como root donde el cliente.
- **`docs/INSTALL.md`**: guía de instalación técnica paso a paso (MySQL, BD, usuario dedicado, config, checklist post-instalación, migración de PC, problemas comunes).
- **`docs/MANUAL.md`**: manual del usuario final pantalla por pantalla, en lenguaje no técnico (pedido de Yuber), con la rutina de respaldo destacada y preguntas frecuentes.
- **Ícono de la aplicación** (`Assets/facontrol.ico`): cuadrado redondeado indigo con la "P" del logo, 7 tamaños (16–256px). Se ve en el Explorador, la barra de tareas, las ventanas y el instalador — adiós al ícono de "app desconocida".

## [0.5.0] — 2026-07-10 · Fase 6 (Reportes, Historial, Configuración) + Notificador de vencimientos

### Added
- **Reporte "Ingresos por período"** (fiel al mockup): rango de fechas con atajos (Este mes / Mes pasado / Trimestre / Año), KPIs — Ganancia (interés cobrado, card indigo), Capital recuperado, Total cobrado, Cuotas cobradas "X de Y programadas" —, gráfico de barras apiladas por semana (interés+capital) y desglose semanal con fila de totales. Botón Exportar Excel.
- **Historial**: visor de solo lectura de la auditoría con filtros por fecha, entidad y acción (límite 300, aviso para afinar filtros).
- **Configuración**:
  - Cambio de contraseña (actual + nueva + confirmación, errores inline).
  - **Tamaño de texto Pequeño/Mediano/Grande** (pedido de Yuber): escala toda la UI (1.0/1.12/1.25) al instante y persiste en `ajustes.json`.
  - **Respaldo/restauración** de la BD con mysqldump/mysql (búsqueda automática del binario, contraseña por MYSQL_PWD, doble confirmación al restaurar).
  - **Exportación a Excel** (ClosedXML 0.105, MIT): libro .xlsx con hojas Clientes/Préstamos/Cuotas/Pagos/Auditoría; manual y **automática** al abrir la app (cada N días configurable, carpeta elegible, activable).
- **Notificador de vencimientos** (pedido del cliente, estilo POS-400): al iniciar sesión —y al cambiar el día de negocio con la app abierta— avisa qué clientes se pasaron de su fecha de pago (lista en rojo con cuotas, monto y fecha). Botón OK + checkbox "No volver a preguntar por este cliente" (persistente, individual). Activable y con "restablecer silenciados" en Configuración.

### Decisiones
- El mockup de Reportes define UN reporte (Ingresos por período); se implementó ese. Los "6 tipos" del plan original quedan abiertos a definirse con el cliente (BLOCKERS.md).
- La migración de PC recomendada es Respaldar/Restaurar (.sql, conserva ids y relaciones); el Excel es de consulta. Importar desde Excel se descartó por riesgo de integridad (BLOCKERS.md).

## [0.4.0] — 2026-07-10 · Fase 5 (Dashboard) + ajustes finos de UI

### Added
- **Panel de control** (pantalla de inicio real): 4 KPIs — Capital colocado (saldo por cobrar de activos), Cobros del mes con **delta vs mes anterior** (↑/↓ %), Clientes activos, Morosidad en RD$ y % del capital.
- **Panel de alertas de cobro** (60%): cuotas vencidas, en mora o que vencen en ≤ 7 días, con pill de semáforo y botón **Cobrar** que navega directo a Cobros con el préstamo preseleccionado.
- **Gráfico de cobros diarios** del mes en curso (LiveChartsCore 2.0.5, barras indigo redondeadas, días sin cobros en 0).
- **Últimos movimientos**: los 10 pagos más recientes.
- `DashboardRepository`/`DashboardService`: agregados en una sola pasada; límites de mes calculados en hora de negocio RD (UTC-4) y convertidos a UTC.

### Changed (ajustes finos pedidos por Yuber)
- Encabezados "Estado", "Nombre" (Clientes) y "Cliente" (Cobros) alineados a la izquierda; el resto sigue centrado.
- Columnas de acciones más anchas: "Ver detalle" y "Ver ficha" ya no se cortan.
- ComboBox: texto centrado verticalmente en toda la app.
- Formularios de Nuevo préstamo y Registrar pago: aire de 12px entre los inputs y el scrollbar.
- App y Views ahora apuntan a `net8.0-windows10.0.19041.0` (asset moderno de SkiaSharp, sin warnings NU1701).

## [0.3.1] — 2026-07-10 · Pulido de UI (10 observaciones de Yuber)

### Added
- **Totales del grid de Préstamos**: 4 cards (capital prestado, por cobrar en activos, total cobrado, préstamos activos) que se recalculan con cada búsqueda/filtro.
- **Filtros rápidos**: por estado en Préstamos (Todos/Activos/Pagados/Cancelados) y por situación en Clientes (con/sin préstamos activos, con saldo pendiente).
- **Iconos en el sidebar** con glifos nativos Segoe MDL2 Assets (sin dependencias).

### Changed
- El **sidebar refleja la navegación interna**: crear un préstamo te lleva al detalle y ahora marca "Préstamos" (antes quedaba en "Nuevo préstamo").
- **ScrollBars delgados (6px)** en toda la app, con thumb redondeado y hover — discretos pero funcionales.
- **Encabezados de tabla centrados** y **scroll horizontal** en las tablas cuando las columnas no caben (el usuario también puede redimensionar columnas).
- La ventana principal abre **maximizada** por defecto.
- Botón de cerrar sesión ahora es **circular** con hover indigo.

### Pendiente registrado
- Documentación final detallada y fácil de entender → Fase 7. Filtros del Historial → Fase 6.

## [0.3.0] — 2026-07-10 · Fase 2 (Clientes) + ajustes de UI

### Added
- **Módulo Clientes completo**: lista con búsqueda (nombre/cédula/teléfono) y agregados por SQL, ficha con 5 métricas (total prestado, cobrado, saldo, préstamos activos, cuotas vencidas) + datos de contacto + sus préstamos, formulario nuevo/editar con validación inline.
- **`ClienteService`**: normalización de cédula dominicana (11 dígitos → `000-0000000-0`; pasaportes se aceptan tal cual), unicidad de cédula amigable, soft delete **bloqueado si hay préstamos activos**, auditoría de crear/modificar/eliminar.
- Flujo ficha → "+ Nuevo préstamo" con el cliente preseleccionado en el wizard.
- Tests: 8 unitarios de normalización de cédula + 2 de integración (CRUD protegido y métricas).

### Fixed
- **LoginWindow**: `SizeToContent="Height"` — en el wizard de primer arranque el botón "Crear cuenta" quedaba cortado por la altura fija (reporte de Yuber).

### Notas
- Pedidos nuevos registrados para Fase 6 (Configuración): tamaño de texto (Pequeño/Mediano/Grande) y exportar/importar datos a Excel con export automático programable. Ver TODO.md.

## [0.2.0] — 2026-07-10 · Fase 3 completa (Préstamos) + Fase 4 (Cobros)

### Added
- **Persistencia de préstamos**: `PrestamoRepository`, `ClienteRepository` (lectura), `PagoRepository`, `ContadorRepository` (correlativos atómicos con `SELECT ... FOR UPDATE`).
- **`PrestamoService`**: creación atómica (contador → prestamo → N cuotas → auditoría en UNA transacción, código P-0001) y cancelación (cuotas impagas → `'cancelada'`, jamás se borran).
- **`PagoService`**: los 4 escenarios de cobro — pago exacto, abono parcial (primero interés, luego capital), adelanto en cascada y liquidación anticipada (cuotas futuras pagan solo capital; el interés futuro se exonera). Todo cobro es una transacción con cuotas bloqueadas (`FOR UPDATE`) y `numero_recibo` R-000001 atómico.
- **UI Préstamos**: lista con búsqueda y agregados en SQL, detalle con métricas + tabla de cuotas con semáforo (indicador rojo lateral en vencidas), wizard "Nuevo préstamo" con vista previa de amortización EN VIVO.
- **UI Cobros**: selector de préstamo activo, cuotas pendientes, atajos (cuota completa / liquidar), preview de cómo se distribuye el monto antes de confirmar, historial de pagos recientes.
- **Recibo 80mm** (patrón imagen del POS-400): el mismo visual se muestra, se imprime (`PrintVisual`) y se exporta a PDF (PdfSharp 6.1.1, rasterizado a 192 DPI).
- **Navegación por páginas**: ContentControl + DataTemplates (ViewModel → View); flujos lista → detalle → cobros cableados por eventos.
- **`IDialogService`** inyectable (confirmaciones/errores testeables; MessageBox solo en la capa App).
- Estilos DESIGN.md: tablas (header uppercase, filas 44px, hover, selección indigo), pills de semáforo y de estado de préstamo, botones Destructivo/Terciario.
- **Tests**: 17 nuevos de distribución de pagos (57 unitarios en total) + 2 de integración contra `facontrol_test` (BD real recreada por corrida: flujo completo crear → abonar → adelantar → liquidar, y cancelación).

### Decisiones
- **Liquidación anticipada** exonera el interés de cuotas futuras (solo se cobra su capital pendiente). Corregible con el cliente — ver BLOCKERS.md.
- **Recibos multi-cuota**: `pago.numero_recibo` es UNIQUE por fila; un cobro que afecta varias cuotas genera un recibo por cuota y el impreso agrupa la operación bajo el primer número.
- PdfSharp 6.1.1 para el PDF del recibo (el mismo visual WPF rasterizado — papel y archivo idénticos).

## [0.1.0] — 2026-07-10 · Fase 1 (Cimientos) + Fase 3 núcleo (Amortización)

### Added
- Solución .NET 8 con arquitectura por capas: App / Views / ViewModels / Models / Services / Data / Common / Printing + 2 proyectos de tests (xUnit + FluentAssertions 7).
- Esquema MySQL `facontrol_db` (8 tablas: usuario, sesion, cliente, prestamo, cuota, pago, auditoria, contador) con scripts de creación, seed Dev y rollback.
- `AmortizacionService`: interés simple dominicano (default) y sistema francés, con redondeo AwayFromZero y ajuste en última cuota. 100% decimal, sin double.
- `CuotaEstadoCalculator`: semáforo de cobros (al día / por vencer / vencida / en mora / pagada / cancelada) con 100% de ramas testeadas.
- 40 tests unitarios verdes (incluye validación exacta contra el mockup: 75,000 al 5% × 12 → cuota 8,461.91).
- Autenticación mono-usuario: wizard de primer arranque, login BCrypt cost 12, rate-limiting (5 intentos → bloqueo 5 min), registro de sesiones, cambio de contraseña.
- `AuditoriaService` con variante transaccional para operaciones multi-paso.
- Shell WPF: LoginWindow + MainWindow con sidebar de 240px (8 secciones) según DESIGN.md (paleta indigo, tipografía, estilos de botones/inputs/cards, converters de moneda RD$ y fecha).
- Serilog a archivo rotativo diario en `logs/`.
- Documentación: `docs/AMORTIZATION.md`, `PTV300-PATTERNS.md`, `TODO.md`, `BLOCKERS.md`.

### Decisiones
- Tasa de interés se ingresa MENSUAL y se convierte por período (÷2 quincenal, ÷4 semanal, ÷30 diaria) — ver AMORTIZATION.md §1.
- ENUM `cuota.estado` amplía la spec con `'cancelada'` (exigido por la regla de cancelación de préstamos).
- FluentAssertions fijado en 7.2.0 (la v8 requiere licencia comercial de pago).
