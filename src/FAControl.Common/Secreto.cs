using System.Security.Cryptography;
using System.Text;

namespace FAControl.Common;

/// <summary>
/// Cifra secretos (la contraseña de aplicación de Gmail) con DPAPI de Windows.
/// El blob solo se puede descifrar en el MISMO usuario y PC que lo cifró, así
/// que la contraseña nunca queda en texto plano en ajustes.json.
/// </summary>
public static class Secreto
{
    private static readonly byte[] Entropia = Encoding.UTF8.GetBytes("FAControl.Gmail.v1");

    /// <summary>Texto plano → base64 cifrado. Cadena vacía si la entrada es vacía.</summary>
    public static string Proteger(string? textoPlano)
    {
        if (string.IsNullOrEmpty(textoPlano))
            return string.Empty;
        var cifrado = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(textoPlano), Entropia, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(cifrado);
    }

    /// <summary>base64 cifrado → texto plano. Cadena vacía si no se puede descifrar.</summary>
    public static string Revelar(string? cifradoBase64)
    {
        if (string.IsNullOrEmpty(cifradoBase64))
            return string.Empty;
        try
        {
            var bytes = ProtectedData.Unprotect(
                Convert.FromBase64String(cifradoBase64), Entropia, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            // Blob de otro usuario/PC o corrupto: se pide la contraseña de nuevo
            return string.Empty;
        }
    }
}
