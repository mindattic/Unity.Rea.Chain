using System;
using System.Collections.Generic;

namespace MindAttic.Chain.Core
{
    /// <summary>
    /// Chain.vb's <c>theMaceMode</c>: what the mace is drawn as. The original declares <c>SpikeBall</c> and
    /// <c>Magnet</c> and starts <c>maceMode</c> on <c>SpikeBall</c>, but nothing ever reads or sets it, Textures.vb
    /// loads only mace.png (<c>texMace</c>) and mace2.png (<c>texMaceGrapple</c>), and magnet.PNG only ships with the
    /// project, so the magnet could never be seen (BIBLE D4). The port makes it a skin (UNI-T43): which picture the
    /// mace wears, chosen by chain.json's <c>maceSkins.mode</c> and flipped by the <see cref="ToggleMaceMode"/> debug
    /// key. It changes nothing else: the hit, the catch and the throw are the same whatever the mace wears.
    /// </summary>
    public enum MaceMode
    {
        /// <summary>gfx/chain/mace/mace.png, the spiked ball: what the original always drew.</summary>
        SpikeBall,

        /// <summary>gfx/chain/mace/magnet.PNG, declared and shipped by the original but never reachable.</summary>
        Magnet,
    }

    /// <summary>The mace skins in the order the debug key steps through them.</summary>
    public static class MaceModes
    {
        private static readonly MaceMode[] Order = (MaceMode[])Enum.GetValues(typeof(MaceMode));

        /// <summary>Every mode, in declaration order (Chain.vb's: SpikeBall, Magnet).</summary>
        public static IReadOnlyList<MaceMode> All => Order;

        /// <summary>The mode after <paramref name="mode"/>, wrapping round: the debug key's next skin.</summary>
        public static MaceMode Next(MaceMode mode)
        {
            var index = Array.IndexOf(Order, mode);
            return Order[(index + 1) % Order.Length];
        }
    }

    /// <summary>
    /// chain.json's <c>maceSkins</c> (UNI-T43): the skin every mace starts in (<c>mode</c>, the setting) and each
    /// skin's picture as Assets/Unity.Rea.Chain/Art/Chain holds it (<c>files</c>, one for every <see cref="MaceMode"/>).
    /// </summary>
    public sealed class MaceSkinData
    {
        private readonly Dictionary<MaceMode, string> _files = new Dictionary<MaceMode, string>();

        /// <summary>The skin the mace starts in (Chain.vb's <c>maceMode As theMaceMode = theMaceMode.SpikeBall</c>).</summary>
        public MaceMode Mode { get; private set; }

        /// <summary>
        /// Input.vb's <c>texMaceGrapple</c> (Textures.vb's mace2.png): the picture shown over whichever
        /// <see cref="MaceMode"/> is current while the grapple is armed, reverting to that mode's own
        /// picture on release (chain.json's <c>maceSkins.files.grapple</c>, U.CHN-T72).
        /// </summary>
        public string GrappleFile { get; private set; }

        /// <summary>The picture file of <paramref name="mode"/> (mace.png), or null if there is none.</summary>
        public string File(MaceMode mode) => _files.TryGetValue(mode, out var file) ? file : null;

        internal static MaceSkinData Read(SettingsReader r)
        {
            var data = new MaceSkinData { Mode = r.Choice<MaceMode>("mode") };

            var files = r.Section("files");
            foreach (var mode in MaceModes.All)
            {
                data._files[mode] = files.Text(mode.ToString());
            }

            data.GrappleFile = files.Text("grapple");
            files.Done();
            return data;
        }
    }
}
