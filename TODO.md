# TODO.md — FAControl

> Actualizado: 2026-07-10 (cierre de Fases 2, 3 y 4)

## ✅ Fase 2 — Clientes (COMPLETA)
- [x] Lista con búsqueda por nombre/cédula/teléfono + agregados (préstamos activos, saldo)
- [x] Ficha: 5 métricas + contacto + préstamos del cliente + doble click al detalle
- [x] Formulario nuevo/editar con validación inline (cédula normalizada a 000-0000000-0)
- [x] Soft delete protegido: no se elimina un cliente con préstamos activos
- [x] Auditoría de crear/modificar/eliminar
- [x] "+ Nuevo préstamo" desde la ficha preselecciona el cliente en el wizard
- [x] Fix: LoginWindow crece con el contenido (botón cortado en el wizard inicial)

## ✅ Fase 1 — Cimientos (COMPLETA)
- [x] Solución `.sln` con 8 proyectos src + 2 de tests, regla de dependencias respetada
- [x] `App.config` externo con cadena de conexión (Dev: root local)
- [x] `001_create_schema.sql` ejecutado en MySQL local (8 tablas) + seed + rollback
- [x] `ConexionFactory` (CConexion adaptado: async, config externa, sin UI)
- [x] Autenticación BCrypt cost 12: wizard de cuenta inicial + login + rate-limiting (5 intentos → 5 min)
- [x] `SesionActual` estático simplificado (Id, Username, Nombre, LoginAt, SesionId)
- [x] `AuditoriaService` funcional (con variante transaccional para operaciones multi-paso)
- [x] `MainWindow` con sidebar navegable (8 secciones)
- [x] DESIGN.md aplicado: paleta, tipografía, estilos de botón/input/card/sidebar, MoneyConverter, DateConverter
- [x] `PTV300-PATTERNS.md` documentado

## ✅ Fase 3 — Préstamos y amortización (COMPLETA)
- [x] `AmortizacionService`: interés simple dominicano (default) + sistema francés (40 tests)
- [x] `CuotaEstadoCalculator` (semáforo) con 100% de ramas cubiertas
- [x] `docs/AMORTIZATION.md` con la matemática y la decisión de convención de tasa
- [x] `PrestamoRepository` + `ClienteRepository` + `ContadorRepository` (`SELECT ... FOR UPDATE`)
- [x] `PrestamoService.CrearAsync`: transacción atómica contador → prestamo → N cuotas → auditoría
- [x] Generación de `codigo` P-0001 vía tabla `contador`
- [x] `PrestamoService.CancelarAsync`: cuotas impagas → 'cancelada' (nunca se borran)
- [x] Wizard "Nuevo préstamo" con vista previa de amortización EN VIVO + resumen
- [x] Lista de préstamos (búsqueda, agregados por SQL, pills de estado)
- [x] Detalle de préstamo (métricas, contrato, tabla de cuotas con semáforo e indicador rojo en vencidas)
- [x] Navegación por páginas (ContentControl + DataTemplates): lista → detalle → cobros

## ✅ Fase 4 — Cobros (COMPLETA)
- [x] `PagoService`: pago exacto, abono parcial (interés primero), adelanto en cascada,
      liquidación anticipada (exonera interés futuro) — 17 tests de la lógica pura
- [x] `numero_recibo` atómico R-000001 (contador + FOR UPDATE, dentro de la transacción del cobro)
- [x] Transacción de cobro: cuotas FOR UPDATE → N pagos → update cuotas → estado préstamo → auditoría
- [x] Módulo Cobros: selector de préstamo activo, cuotas pendientes con semáforo,
      atajos (cuota completa / liquidar), preview de distribución, pagos recientes
- [x] Recibo 80mm como visual WPF (patrón imagen POS-400): vista previa + impresión (PrintVisual) + PDF (PdfSharp)
- [x] Tests de integración contra `facontrol_test`: flujo completo crear → abonar → adelantar → liquidar; cancelación

## ✅ Fase 5 — Dashboard (COMPLETA)
- [x] 4 KPIs: capital colocado, cobros del mes (delta vs mes anterior), clientes activos, morosidad RD$ + %
- [x] Panel de alertas de cobro con semáforo y botón "Cobrar" → Cobros preseleccionado
- [x] Gráfico de cobros diarios del mes (LiveChartsCore 2.0.5)
- [x] Últimos movimientos (10 pagos recientes)

## ✅ Fase 6 — Reportes, Historial y Configuración (COMPLETA)
- [x] Reporte "Ingresos por período" fiel al mockup (KPIs, barras apiladas por semana, desglose, export Excel)
- [x] Historial de auditoría con filtros por fecha, entidad y acción
- [x] Configuración: cambio de contraseña
- [x] Tamaño de texto Pequeño/Mediano/Grande (persistente, escala toda la UI)
- [x] Respaldo/restauración de BD (mysqldump/mysql, doble confirmación)
- [x] Export Excel completo (manual + automático cada N días, activable)
- [x] **Notificador de vencimientos** (pedido del cliente): aviso al iniciar + cambio de día,
      silenciado individual por cliente, activable en Configuración
- Nota: los "6 reportes" del plan original quedaron como el único mockup diseñado — más tipos a definir con el cliente (BLOCKERS #7). Importar desde Excel descartado (BLOCKERS #8).

## ✅ Fase 7 — Empaquetado y entrega (COMPLETA)
- [x] `dotnet publish` self-contained win-x64 (no requiere .NET en la PC del cliente)
- [x] Instalador Inno Setup 6 en español: `installer/Output/FAControl_Setup_1.0.0.exe`
- [x] `scripts/db/003_crear_usuario_dedicado.sql` (usuario `facontrol` con permisos mínimos)
- [x] `docs/INSTALL.md` (guía técnica) + `docs/MANUAL.md` (manual no técnico pantalla por pantalla)
- [x] Proyecto copiado a `Freelancer - Claude Save\`

## 📌 Mejoras futuras (post-entrega, si el cliente las pide)
- [ ] Logout que regrese al login sin cerrar la app (BLOCKERS #3)
- [ ] Pago compensatorio negativo con UI propia (hoy: corrección asistida por técnico)
- [ ] Más tipos de reportes (BLOCKERS #7)
- [ ] Empaquetar fuente Inter .ttf (hoy usa fallback Segoe UI)
- [ ] Datos del negocio (RNC, dirección, teléfono) configurables para el encabezado del recibo
- [ ] Logout que regrese al login sin cerrar la app (hoy cierra la app — decisión pendiente)
- [ ] Empaquetar fuente Inter .ttf (hoy usa fallback Segoe UI)
- [ ] Sidebar: marcar el ítem activo cuando la navegación no viene de un click (detalle/nuevo)
- [ ] Pago compensatorio negativo (corrección de errores) con nota obligatoria — regla definida, UI pendiente
