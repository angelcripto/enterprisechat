using System.Reflection;
using System.Runtime.Loader;
using EnterpriseChat.Licensing.Abstractions;

namespace EnterpriseChat.Server.Licensing;

/// <summary>
/// Wires the licensing pipeline into DI. Looks for a commercial Pro plugin
/// implementing <see cref="ILicenseValidator"/> and <see cref="ILicenseAdministrator"/>
/// under <c>plugins/</c> next to the server binary. If none is found, falls back
/// to the open-source <see cref="FreeLicenseValidator"/> + <see cref="FreeLicenseAdministrator"/>.
/// </summary>
internal static class LicensingExtensions
{
    public static IServiceCollection AddEnterpriseChatLicensing(
        this IServiceCollection services,
        IConfiguration _config,
        IHostEnvironment env)
    {
        var plugin = TryLoadPlugin(env);

        var validator = plugin?.Validator ?? new FreeLicenseValidator();
        var administrator = plugin?.Administrator ?? new FreeLicenseAdministrator();

        services.AddSingleton<ILicenseValidator>(validator);
        services.AddSingleton<ILicenseAdministrator>(administrator);
        return services;
    }

    private static PluginInstance? TryLoadPlugin(IHostEnvironment env)
    {
        var pluginsDir = Path.Combine(env.ContentRootPath, "plugins");
        if (!Directory.Exists(pluginsDir))
        {
            return null;
        }

        foreach (var dll in Directory.EnumerateFiles(pluginsDir, "EnterpriseChat.Licensing.*.dll"))
        {
            if (Path.GetFileNameWithoutExtension(dll)
                .Equals("EnterpriseChat.Licensing.Abstractions", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Assembly asm;
            try
            {
                asm = AssemblyLoadContext.Default.LoadFromAssemblyPath(dll);
            }
            catch (Exception)
            {
                continue;
            }

            var types = asm.GetTypes();

            var validatorType = types.FirstOrDefault(t =>
                typeof(ILicenseValidator).IsAssignableFrom(t)
                && t is { IsClass: true, IsAbstract: false });
            var adminType = types.FirstOrDefault(t =>
                typeof(ILicenseAdministrator).IsAssignableFrom(t)
                && t is { IsClass: true, IsAbstract: false });

            if (validatorType is null)
            {
                continue;
            }

            var validator = Activator.CreateInstance(validatorType) as ILicenseValidator;
            if (validator is null)
            {
                continue;
            }

            var administrator = adminType is null
                ? null
                : Activator.CreateInstance(adminType) as ILicenseAdministrator;

            return new PluginInstance(validator, administrator);
        }

        return null;
    }

    private sealed record PluginInstance(ILicenseValidator Validator, ILicenseAdministrator? Administrator);
}
