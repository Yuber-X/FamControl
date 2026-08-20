# Correo automático — FAControl

**Familia Almonte Auto Import SRL**
Versión del programa: 2.0.2 · Fecha de esta guía: 20 de agosto de 2026

> Guía paso a paso para configurar **Configuración → Recordatorios por correo**.
> Se hace **una sola vez**. Toma unos 10 minutos.
>
> Cuenta que se está configurando: **familiaalmonteautoimport@gmail.com**

---

## 0. El problema que estás viendo (y por qué pasa)

Al entrar a `myaccount.google.com/apppasswords` Google muestra:

> *"The setting you are looking for is not available for your account."*
> (*"La configuración que buscas no está disponible para tu cuenta."*)

**No es un error de FAControl ni de la cuenta.** Google esconde esa página
cuando la cuenta **no tiene activada la verificación en 2 pasos**. Las
contraseñas de aplicación son un "permiso extra" del sistema de 2 pasos: si el
sistema no está prendido, la página no existe.

Entonces el orden correcto es:

```
1) Prender la verificación en 2 pasos   ← lo que falta
2) Recién ahí aparece "Contraseñas de aplicaciones"
3) Generar las 16 letras
4) Pegarlas en FAControl
```

---

## 1. Prender la verificación en 2 pasos

1. Abrí el navegador **con la sesión de `familiaalmonteautoimport@gmail.com`**.
   - Si tenés varias cuentas de Google abiertas, cerrá las demás o usá una
     ventana de incógnito. **Este es el error más común**: se configura la
     cuenta personal en vez de la del negocio.
   - Para confirmar en cuál estás: entrá a `myaccount.google.com` y mirá el
     correo que aparece debajo del nombre. Tiene que decir
     `familiaalmonteautoimport@gmail.com`.

2. Entrá a:

   ```
   https://myaccount.google.com/signinoptions/twosv
   ```

3. Tocá **"Comenzar"** / **"Get started"**.

4. Google pide la contraseña normal de la cuenta. Escribila.

5. Pide un **teléfono** para mandar el código:
   - Escribí el número del negocio (formato `+1 809 xxx xxxx`).
   - Elegí **Mensaje de texto (SMS)**.
   - Tocá **Enviar**.

6. Llega un SMS con un código de 6 dígitos. Escribilo y tocá **Siguiente**.

7. Google muestra **"Activar la verificación en 2 pasos"** → tocá **Activar**.

✅ Listo. Ahora la cuenta pide un código cuando alguien entra desde un equipo
nuevo. **La contraseña de siempre no cambia** y el correo se sigue usando igual.

> **Guardá el teléfono que usaste.** Si se pierde ese número, recuperar la
> cuenta se complica. Anotalo junto a la contraseña.

---

## 2. Generar la contraseña de aplicación

1. Ahora sí, entrá a:

   ```
   https://myaccount.google.com/apppasswords
   ```

   Esta vez **sí** abre (si sigue diciendo que no está disponible, esperá 2–3
   minutos y recargá: Google tarda un poco en habilitarla).

2. En **"Nombre de la aplicación"** escribí:

   ```
   FAControl
   ```

   > El nombre es solo una etiqueta para que después sepas de dónde salió. No
   > afecta en nada.

3. Tocá **Crear** / **Create**.

4. Google muestra un recuadro amarillo con **16 letras en 4 grupos**, algo así:

   ```
   abcd efgh ijkl mnop
   ```

   **Copialas ahora**: al cerrar ese recuadro **no se pueden volver a ver**. Si
   se pierden, no pasa nada grave — se borra esa y se crea otra.

---

## 3. Pegarlas en FAControl

1. Abrí FAControl → **Configuración** → bajá hasta **Recordatorios por correo**.

2. Marcá la casilla de activar los recordatorios.

3. Completá:

   | Campo | Qué va |
   |---|---|
   | **Cuenta Gmail (remitente)** | `familiaalmonteautoimport@gmail.com` |
   | **Contraseña de aplicación** | las **16 letras** del paso 2 |
   | **Correo del dueño** | a dónde llega el resumen diario |
   | **Días de anticipación** | con cuántos días de antelación avisar la cuota |

   > Los **espacios se quitan solos**: se puede pegar `abcd efgh ijkl mnop` tal
   > cual, no hace falta juntarlas.

4. Tocá **Enviar prueba**.
   - Si dice **"Correo de prueba enviado"** → terminaste. ✅
   - Si da error, mirá la tabla de abajo.

5. Si querés que salgan solos: marcá **"Enviar automáticamente al abrir la
   aplicación (una vez al día)"**.

> Al volver a entrar a Configuración, el campo de la contraseña se ve **vacío**.
> Es a propósito (nunca se muestra una clave guardada). Debajo aparece
> **"✓ Contraseña guardada"**: eso confirma que sigue ahí. Si la dejás vacía y
> guardás, se conserva la anterior.

---

## 4. Si algo falla

| Lo que dice | Qué pasó | Cómo se arregla |
|---|---|---|
| *"The setting you are looking for is not available for your account"* | La verificación en 2 pasos está apagada, **o** estás en otra cuenta de Google | Hacé el paso 1. Revisá en `myaccount.google.com` que el correo sea el del negocio |
| *"Gmail rechazó el usuario o la contraseña"* | Se puso la contraseña **normal** de Gmail en vez de las 16 letras | Volvé al paso 2 y generá la contraseña de aplicación |
| Sigue rechazando con las 16 letras | La contraseña de aplicación fue revocada (pasa si se cambió la clave de la cuenta) | Generá una nueva y pegala de nuevo |
| *"El correo no está configurado"* | Falta la cuenta o la contraseña | Completá los dos campos del paso 3 |
| La prueba sale bien pero el cliente no recibe nada | El correo del cliente está mal escrito o vacío en su ficha | Clientes → editar → revisar el correo. Revisar también **Spam** |
| Se manda dos veces el mismo día | No pasa: el envío automático se marca por fecha y no se repite | — |

---

## 5. Datos técnicos (para el técnico, no para el cliente)

- Servidor: `smtp.gmail.com`, puerto **587**, **STARTTLS** (`EmailService.cs`).
- La contraseña de aplicación se guarda **cifrada con DPAPI** (`Secreto.cs`),
  atada al **usuario de Windows y a esa PC**. No viaja en el respaldo ni en
  texto plano.
  - **Consecuencia:** si se cambia de PC o de cuenta de Windows, **hay que
    volver a pegar las 16 letras**. No es que se haya perdido: es que ese
    equipo no puede descifrar el secreto del otro.
- El envío automático corre en segundo plano al abrir la aplicación, una vez al
  día. Qué manda depende del modo:
  - **PrestControl / DealControl**: recordatorio de cuota a cada cliente con
    vencimiento próximo + resumen al dueño.
  - **POS-500**: aviso al dueño de la mercancía por caducar.
- Si el envío automático falla, el motivo queda visible en Configuración hasta
  que un envío salga bien (desde 2.0.0). Antes solo iba al log.
- Límite de Gmail gratuito: **~500 destinatarios por día**. Con una cartera
  normal no se llega ni cerca.

---

*FAControl · desarrollado por Yuber Santana · soporte según contrato de mantenimiento.*
