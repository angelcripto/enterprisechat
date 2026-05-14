using System.Text;
using System.Text.Json;
using EnterpriseChat.Server.Crypto;

namespace EnterpriseChat.Server.Bootstrap;

/// <summary>
/// Garantiza que existe una <c>EnterpriseChat:Crypto:MasterKey</c> antes de
/// que ningún componente del pipeline intente cifrar credenciales de
/// proveedores externos. Si la clave falta, se genera 32 bytes aleatorios
/// y se escriben a <c>appsettings.Production.json</c> en el ContentRoot.
///
/// Comportamiento por entorno:
///   - Production: si falta, se genera y se persiste. El operador no
///     tiene que hacer nada manualmente.
///   - Development: si falta, se genera EN MEMORIA pero NO se persiste
///     (evita ensuciar el repo). El dev tiene que persistirlo a mano si
///     quiere consistencia entre arranques.
/// </summary>
internal static class MasterKeyInitializer
{
    private const string ConfigPath = "EnterpriseChat:Crypto:MasterKey";

    public static byte[] EnsureMasterKey(IConfigurationManager config, IHostEnvironment env, ILogger logger)
    {
        var existing = config[ConfigPath];
        if (!string.IsNullOrWhiteSpace(existing))
        {
            return AppCrypto.DecodeKey(existing);
        }

        var newKey = AppCrypto.GenerateBase64Key();
        logger.LogWarning("EnterpriseChat:Crypto:MasterKey no estaba definida; se ha generado una nueva.");

        if (env.IsProduction())
        {
            PersistToProductionSettings(env.ContentRootPath, newKey, logger);
        }
        else
        {
            logger.LogWarning(
                "Entorno {Env}: la nueva master key se mantiene SOLO en memoria. " +
                "Si quieres persistencia entre reinicios, copia este valor a appsettings.{Env}.json:\n  {Key}",
                env.EnvironmentName, env.EnvironmentName, newKey);
        }

        // Inyectar en la configuracion en memoria para que cualquier
        // resolución posterior dentro del mismo proceso vea la clave.
        config[ConfigPath] = newKey;
        return AppCrypto.DecodeKey(newKey);
    }

    private static void PersistToProductionSettings(string contentRoot, string base64Key, ILogger logger)
    {
        var path = Path.Combine(contentRoot, "appsettings.Production.json");

        JsonDocument? doc = null;
        if (File.Exists(path))
        {
            try
            {
                using var stream = File.OpenRead(path);
                doc = JsonDocument.Parse(stream);
            }
            catch (JsonException ex)
            {
                // appsettings.Production.json roto: no lo pisamos. Logueamos
                // el problema en lugar de borrar config del usuario.
                logger.LogError(ex,
                    "No se pudo parsear {Path}. La nueva MasterKey queda solo en memoria. " +
                    "Arregla el JSON y reinicia para persistirla.", path);
                return;
            }
        }

        var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            var hasEnterpriseChat = false;

            if (doc is not null)
            {
                foreach (var property in doc.RootElement.EnumerateObject())
                {
                    if (property.NameEquals("EnterpriseChat"))
                    {
                        hasEnterpriseChat = true;
                        writer.WritePropertyName("EnterpriseChat");
                        WriteEnterpriseChatWithCrypto(writer, property.Value, base64Key);
                    }
                    else
                    {
                        property.WriteTo(writer);
                    }
                }
            }

            if (!hasEnterpriseChat)
            {
                writer.WritePropertyName("EnterpriseChat");
                writer.WriteStartObject();
                writer.WritePropertyName("Crypto");
                writer.WriteStartObject();
                writer.WriteString("MasterKey", base64Key);
                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }
        doc?.Dispose();

        File.WriteAllBytes(path, buffer.ToArray());
        logger.LogInformation("MasterKey persistida en {Path}.", path);
    }

    private static void WriteEnterpriseChatWithCrypto(
        Utf8JsonWriter writer,
        JsonElement enterpriseChatNode,
        string base64Key)
    {
        writer.WriteStartObject();
        var hasCrypto = false;

        foreach (var prop in enterpriseChatNode.EnumerateObject())
        {
            if (prop.NameEquals("Crypto"))
            {
                hasCrypto = true;
                writer.WritePropertyName("Crypto");
                WriteCryptoWithMasterKey(writer, prop.Value, base64Key);
            }
            else
            {
                prop.WriteTo(writer);
            }
        }

        if (!hasCrypto)
        {
            writer.WritePropertyName("Crypto");
            writer.WriteStartObject();
            writer.WriteString("MasterKey", base64Key);
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }

    private static void WriteCryptoWithMasterKey(
        Utf8JsonWriter writer,
        JsonElement cryptoNode,
        string base64Key)
    {
        writer.WriteStartObject();
        var written = false;
        foreach (var prop in cryptoNode.EnumerateObject())
        {
            if (prop.NameEquals("MasterKey"))
            {
                writer.WriteString("MasterKey", base64Key);
                written = true;
            }
            else
            {
                prop.WriteTo(writer);
            }
        }
        if (!written)
        {
            writer.WriteString("MasterKey", base64Key);
        }
        writer.WriteEndObject();
    }
}
