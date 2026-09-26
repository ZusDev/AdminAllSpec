using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Convars;
using SwiftlyS2.Shared.Memory;
using SwiftlyS2.Shared.Plugins;
using SwiftlyS2.Shared.SchemaDefinitions;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace AdminAllSpec;

[PluginMetadata(Id = "AdminAllSpec", Name = "AdminAllSpec", Version = "1.0.0", Author = "xstage / ported by M1K@c")]
public sealed class Plugin(ISwiftlyCore core) : BasePlugin(core)
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte IsValidObserverTarget(nint services, nint target);

    private IUnmanagedFunction<IsValidObserverTarget>? _function;
    private Guid? _hook;
    private IConVar<string>? _permission;
    private bool _reportedError;

    public override void Load(bool hotReload)
    {
        var address = Core.GameData.GetSignature("AdminAllSpec_IsValidObserverTarget");
        if (address == nint.Zero)
            throw new InvalidOperationException("IsValidObserverTarget signature was not found. Update resources/gamedata/signatures.jsonc for this CS2 build.");

        _permission = Core.ConVar.CreateOrFind("sw_adminallspec_permission", "SwiftlyS2 permission required to spectate both teams", "admin.generic");
        _function = Core.Memory.GetUnmanagedFunctionByAddress<IsValidObserverTarget>(address);
        _hook = _function.AddHook(next => (services, target) =>
        {
            try
            {
                if (CanObserve(services, target))
                    return 1;
            }
            catch (Exception ex)
            {
                if (!_reportedError)
                {
                    _reportedError = true;
                    Core.Logger.LogError(ex, "AdminAllSpec observer check failed, using normal spectator rules.");
                }
            }
            return next()(services, target);
        });
    }

    public override void Unload()
    {
        if (_hook is Guid hook)
            _function?.RemoveHook(hook);
        _hook = null;
        _function = null;
    }

    private bool CanObserve(nint servicesAddress, nint targetAddress)
    {
        if (servicesAddress == nint.Zero || targetAddress == nint.Zero)
            return false;

        var services = Core.Memory.ToSchemaClass<CPlayer_ObserverServices>(servicesAddress);
        var owner = services.__m_pChainEntity.Entity;
        if (!owner.IsValid)
            return false;

        var observerController = owner.As<CCSPlayerPawnBase>().OriginalController.Value;
        if (observerController is not { IsValid: true })
            return false;

        var observer = Core.PlayerManager.GetPlayerFromController(observerController);
        if (observer is not { IsValid: true, IsFakeClient: false } || observer.SteamID == 0)
            return false;

        var target = Core.Memory.ToSchemaClass<CCSPlayerPawn>(targetAddress);
        if (!target.IsValid || target.LifeState != 0)
            return false;

        var targetController = target.OriginalController.Value;
        if (targetController is not { IsValid: true, Connected: PlayerConnectedState.Connected, TeamNum: > 1 })
            return false;

        var permission = _permission?.Value;
        return !string.IsNullOrWhiteSpace(permission) && Core.Permission.PlayerHasPermission(observer.SteamID, permission);
    }
}
