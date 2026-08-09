using System;

namespace Pho.Core
{
    /// <summary>
    /// Each assembly ships its own XInstaller implementing this and
    /// self-registers its services into GameContext -- no shared bootstrap
    /// wiring file to conflict over. The actual reflection-based scanner
    /// that discovers [AutoInstall] types lands with GameBootstrap at M2,
    /// once Boot.unity exists to run it against.
    /// </summary>
    public interface IInstaller
    {
        int Order { get; }
        void Install(GameContext ctx);
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class AutoInstallAttribute : Attribute
    {
    }
}
