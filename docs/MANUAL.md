# Manual de FAControl

> **Su sistema para manejar préstamos, sin complicaciones.**
> Este manual está escrito para usted, que presta dinero — no para técnicos.
> Léalo una vez y téngalo a mano: cada sección explica una pantalla.

---

## 1. Entrar al sistema

Al abrir FAControl aparece la pantalla de entrada.

- **La primera vez**: el sistema le pide crear su cuenta — su usuario, su nombre
  y una contraseña de al menos 8 caracteres. **Apúntela en un lugar seguro**:
  es la llave de su negocio.
- **Las demás veces**: escriba su usuario y contraseña y presione *Iniciar sesión*.
- Si se equivoca de contraseña 5 veces seguidas, el sistema se bloquea 5 minutos
  por seguridad. Espere y vuelva a intentar.

## 2. El aviso de pagos vencidos

Apenas entra, si algún cliente **se pasó de su fecha de pago**, el sistema se lo
avisa con una ventana roja que muestra:

- el nombre del cliente,
- cuántas cuotas debe y cuánto dinero es,
- desde qué fecha está atrasado.

Opciones:
- **OK** — cierra el aviso. Mañana (o la próxima vez que abra) volverá a salir.
- **"No volver a preguntar por este cliente"** — marque esa casilla si ya sabe
  de ese caso y no quiere que se lo repita. Solo silencia a ESE cliente: si otro
  se atrasa, el aviso saldrá igual con el nuevo nombre.
- ¿Silenció a alguien por error? Vaya a **Configuración → Aviso de pagos
  vencidos → Restablecer silenciados**.

## 3. Panel (la pantalla de inicio)

Es el resumen de su negocio de un vistazo:

| Tarjeta | Qué significa |
|---|---|
| **Capital colocado** | Todo el dinero que está "en la calle" pendiente de cobrar |
| **Cobros del mes** | Lo que ha cobrado este mes, comparado con el mes pasado (↑ verde = mejor) |
| **Clientes activos** | Cuántas personas le deben actualmente |
| **Morosidad** | El dinero atrasado (en rojo) y qué porcentaje representa |

Debajo:
- **Alertas de cobro**: la lista de cuotas atrasadas o que vencen esta semana.
  El botón **Cobrar** de cada línea lo lleva directo a cobrarle a esa persona.
- **El gráfico**: cuánto cobró cada día del mes.
- **Últimos movimientos**: sus 10 cobros más recientes.

## 4. Clientes

Aquí vive su cartera de clientes.

- **Buscar**: escriba nombre, cédula o teléfono en la caja de arriba.
- **+ Nuevo cliente**: nombre, apellido y cédula son obligatorios; el resto es
  opcional. La cédula se acomoda sola al formato 001-1234567-8.
- **Ver ficha** (o doble clic): la historia completa de esa persona — cuánto le
  ha prestado, cuánto le ha pagado, cuánto le debe, y todos sus préstamos.
- Desde la ficha puede **editar** sus datos, abrirle un **nuevo préstamo**, o
  **eliminarlo** (solo si no tiene préstamos activos; su historial nunca se pierde).

## 5. Nuevo préstamo

Complete el formulario de la izquierda y **mire la derecha**: la tabla de cuotas
se calcula sola mientras escribe, ANTES de guardar nada.

| Campo | Qué poner |
|---|---|
| Cliente | Elíjalo de la lista (debe existir en Clientes) |
| Monto | Cuánto le presta (ej. 20,000) |
| Tasa mensual (%) | Su tasa de siempre (ej. 10 = 10% mensual) |
| Cantidad de cuotas | En cuántos pagos |
| Modalidad | Cada cuánto paga: diario, semanal, quincenal o mensual |
| Fecha del primer pago | Cuándo vence la primera cuota |
| Método de cálculo | **Interés fijo (dominicano)** es el normal: el interés se calcula siempre sobre el monto prestado |

Cuando la tabla de la derecha le cuadre, presione **Crear préstamo**. El sistema
le asigna un código (P-0001, P-0002…) y lo lleva al detalle.

## 6. Préstamos

La lista de todos los préstamos con sus totales arriba (capital prestado, por
cobrar, cobrado, activos). Puede buscar por código o cliente y filtrar por estado.

**Ver detalle** muestra el contrato completo y su tabla de cuotas con colores:

| Color | Significado |
|---|---|
| 🟢 Al día | Vence dentro de más de una semana |
| 🟡 Por vencer | Vence hoy o en los próximos 7 días |
| 🟠 Vencida | Se pasó hace 15 días o menos |
| 🔴 En mora | Se pasó hace más de 15 días |
| 🔵 Pagada | Ya está cubierta |

Desde el detalle puede **Registrar pago** o **Cancelar préstamo** (anula las
cuotas que faltan; no se puede deshacer).

## 7. Cobros (registrar pagos)

1. Elija el préstamo en la lista de arriba (aparecen solo los activos).
2. Verá las cuotas pendientes y tres números: la deuda total, lo que vale la
   próxima cuota, y cuánto costaría **liquidar hoy**.
3. Escriba el **monto recibido** — o use los atajos:
   - **Cuota completa**: pone el valor exacto de la próxima cuota.
   - **Liquidar préstamo**: el cliente salda todo hoy — las cuotas futuras pagan
     solo su capital y **el interés que faltaba se le perdona**.
4. Antes de confirmar, el sistema le muestra **cómo se aplicará el dinero**:
   - si el cliente paga menos que la cuota, primero se cobra el interés y el
     resto baja el capital;
   - si paga más, lo que sobra adelanta las cuotas siguientes.
5. **Registrar pago** → sale el **recibo**: puede imprimirlo (impresora de
   ticket de 80mm o normal) o guardarlo como PDF.

> Los pagos registrados **nunca se borran ni se editan**. Si se equivocó,
> consúltelo con su técnico — hay una forma correcta de corregirlo sin dañar
> las cuentas.

## 8. Reportes

**Ingresos por período** le dice cuánto ganó:

1. Elija las fechas (o use *Este mes*, *Mes pasado*, *Trimestre*, *Año*).
2. La tarjeta morada es lo importante: **su ganancia** (el interés cobrado).
   Al lado: el capital que recuperó, el total y cuántas cuotas cobró.
3. El gráfico y la tabla desglosan semana por semana.
4. **Exportar Excel** guarda todos sus datos en un archivo para verlo fuera del sistema.

## 9. Historial

Todo lo que pasa en el sistema queda anotado: quién creó qué, cuándo se cobró,
qué se modificó. Filtre por fechas, tipo o acción. **Nada de aquí se puede
borrar** — es su registro de respaldo ante cualquier duda.

## 10. Configuración

| Sección | Para qué sirve |
|---|---|
| **Apariencia** | Letras Pequeñas, Medianas o Grandes — pruebe cuál le resulta más cómoda |
| **Aviso de pagos vencidos** | Encender/apagar el aviso del inicio y "des-silenciar" clientes |
| **Cambiar contraseña** | Escriba la actual y dos veces la nueva |
| **Respaldo** | ⭐ LO MÁS IMPORTANTE — vea abajo |
| **Exportación a Excel** | Exportar ahora, o automático cada X días a la carpeta que elija |

### El respaldo: hágalo religiosamente

Su negocio completo vive en esta computadora. Si la PC se daña y no hay
respaldo, **no hay forma de recuperar los datos**.

1. Configuración → **Respaldar ahora** → guarde el archivo en una **memoria USB**
   (o en su nube), no en la misma PC.
2. Hágalo **al menos una vez por semana** (mejor: cada día que cobre).
3. Para pasar a una PC nueva: su técnico instala FAControl allá y usa
   **Restaurar desde archivo…** con su último respaldo. Todo vuelve idéntico.

## 11. Preguntas frecuentes

**¿Puedo usar el sistema sin internet?**
Sí. FAControl funciona 100% en su computadora, no necesita internet para nada.

**Le cobré de más / me equivoqué en un pago, ¿lo borro?**
No se puede borrar (así se protegen sus cuentas). Contacte a su técnico para
registrar la corrección correcta.

**¿Qué pasa si un cliente me paga la mitad de la cuota?**
Regístrelo normal: el sistema aplica primero el interés y el resto al capital.
La cuota queda "a medias" y el próximo pago la completa.

**¿Puedo perdonarle el interés a un cliente que quiere saldar?**
Sí — eso es exactamente la **Liquidación anticipada** en Cobros.

**Olvidé mi contraseña.**
Contacte a su técnico: puede restablecerla directamente en la base de datos.

**¿El aviso de vencidos sale muchas veces?**
No: sale una sola vez cada vez que abre el sistema (o al pasar la medianoche
si lo deja abierto).

---
*FAControl v1.0 · desarrollado por Yuber Santana*
