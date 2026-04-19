using System.Collections.Generic;
using Oxide.Core;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Base Dome", "gamezoneone", "1.0.2")]
    [Description("Visualizes the building privilege radius of your base as a transparent sphere.")]
    public class BaseDome : RustPlugin
    {
        private const string PermUse = "basedome.use";

        private PluginConfig _config;
        private readonly Dictionary<ulong, uint> _buildingByPlayer = new Dictionary<ulong, uint>();
        private readonly Dictionary<ulong, Timer> _timers = new Dictionary<ulong, Timer>();

        private class PluginConfig
        {
            public bool AutoGrantDefaultGroup = true;
            public float PaddingMeters = 2f;
            // Zuschlag zum Radius: echte Baufreigabe reicht weiter als die sichtbare Blockhülle (Rust ~16 m).
            public float PrivilegeBeyondBlocksMeters = 16f;
            // Falls die berechnete Kugel zu klein wirkt (alte Config ohne PrivilegeBeyondBlocks = 0): Mindestgröße.
            public float MinimumSphereRadiusMeters = 18f;
            public float DrawIntervalSeconds = 0.25f;
            public float DdrawDurationSeconds = 0.4f;
            public float FallbackRadiusMeters = 16f;
            public float[] SphereColorRgba = { 0.35f, 0.85f, 0.45f, 0.85f };
        }

        private void Init()
        {
            permission.RegisterPermission(PermUse, this);
        }

        private void OnServerInitialized()
        {
            LoadConfigValues();
            if (_config.AutoGrantDefaultGroup)
                permission.GrantGroupPermission("default", PermUse, this);
        }

        private void Unload()
        {
            foreach (var kv in _timers)
                kv.Value?.Destroy();
            _timers.Clear();
            _buildingByPlayer.Clear();
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player != null)
                Cleanup(player.userID);
        }

        protected override void LoadDefaultConfig()
        {
            Config.WriteObject(new PluginConfig(), true);
        }

        private void LoadConfigValues()
        {
            try
            {
                _config = Config.ReadObject<PluginConfig>();
                if (_config == null)
                    throw new System.Exception("null config");
            }
            catch
            {
                _config = new PluginConfig();
            }

            // Fehlende JSON-Keys werden mit 0 deserialisiert — dadurch war die Kuppel winzig.
            var changed = false;
            // Nur dieses Feld: fehlender Key wurde als 0 gelesen → Mini-Kuppel (siehe Screenshot).
            if (_config.PrivilegeBeyondBlocksMeters <= 0f)
            {
                _config.PrivilegeBeyondBlocksMeters = 16f;
                changed = true;
            }

            if (changed)
                Config.WriteObject(_config, true);
        }

        private float EffectiveMinimumRadius()
        {
            return _config.MinimumSphereRadiusMeters > 0f ? _config.MinimumSphereRadiusMeters : 18f;
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["NoPermission"] = "You don't have permission to use /basedome.",
                ["Help"]         = "Use <color=#9f9>/basedome</color> or <color=#9f9>/bd</color> (stand inside your building privilege). Permission: <color=#9f9>basedome.use</color>",
                ["On"]           = "Base dome: <color=#8f8>on</color> (building ID saved).",
                ["Off"]          = "Base dome: <color=#f88>off</color>.",
                ["NoPriv"]       = "You are not in your building privilege here or not authorized at the tool cupboard.",
                ["NoBuilding"]   = "No valid building found for this building privilege.",
                ["BuildingGone"] = "The building no longer exists — dome off.",
                ["AuthLost"]     = "No longer authorized at this TC — dome off.",
            }, this);
        }

        [ChatCommand("basedome")]
        private void ChatCmdBaseDome(BasePlayer player, string command, string[] args)
        {
            ToggleDome(player);
        }

        [ChatCommand("bd")]
        private void ChatCmdBd(BasePlayer player, string command, string[] args)
        {
            ToggleDome(player);
        }

        private void ToggleDome(BasePlayer player)
        {
            if (player == null)
                return;

            if (!permission.UserHasPermission(player.UserIDString, PermUse))
            {
                PrintToChat(player, lang.GetMessage("NoPermission", this, player.UserIDString));
                return;
            }

            var uid = player.userID;

            if (_buildingByPlayer.ContainsKey(uid))
            {
                Cleanup(uid);
                PrintToChat(player, lang.GetMessage("Off", this, player.UserIDString));
                return;
            }

            var priv = player.GetBuildingPrivilege();
            if (priv == null || !priv.IsAuthed(player))
            {
                PrintToChat(player, lang.GetMessage("NoPriv", this, player.UserIDString));
                return;
            }

            var building = priv.GetBuilding();
            if (building == null)
            {
                PrintToChat(player, lang.GetMessage("NoBuilding", this, player.UserIDString));
                return;
            }

            _buildingByPlayer[uid] = building.ID;

            _timers[uid] = timer.Repeat(_config.DrawIntervalSeconds, 0, () => DrawDome(uid));

            PrintToChat(player, lang.GetMessage("On", this, player.UserIDString));
        }

        private void DrawDome(ulong uid)
        {
            var player = BasePlayer.FindByID(uid);
            if (player == null || !player.IsConnected)
            {
                Cleanup(uid);
                return;
            }

            if (!_buildingByPlayer.TryGetValue(uid, out var buildingId))
            {
                Cleanup(uid);
                return;
            }

            if (BuildingManager.server == null ||
                !BuildingManager.server.buildingDictionary.TryGetValue(buildingId, out var building) || building == null)
            {
                PrintToChat(player, lang.GetMessage("BuildingGone", this, player.UserIDString));
                Cleanup(uid);
                return;
            }

            if (!PlayerIsAuthedOnBuilding(player, building))
            {
                PrintToChat(player, lang.GetMessage("AuthLost", this, player.UserIDString));
                Cleanup(uid);
                return;
            }

            if (!TryGetMergedBounds(building, out var merged))
            {
                var tc = FindAnyCupboard(building);
                var center = tc != null ? tc.transform.position : player.transform.position;
                var r = Mathf.Max(
                    _config.FallbackRadiusMeters + _config.PaddingMeters + _config.PrivilegeBeyondBlocksMeters,
                    EffectiveMinimumRadius());
                SendSphere(player, center, r);
                return;
            }

            var c = merged.center;
            // Halbe Raumdiagonale der umschließenden Box (nicht Max(extents) — das wäre zu klein).
            var enclosingHalfDiagonal = merged.extents.magnitude;
            var r2 = enclosingHalfDiagonal + _config.PaddingMeters + _config.PrivilegeBeyondBlocksMeters;
            r2 = Mathf.Max(r2, EffectiveMinimumRadius());
            SendSphere(player, c, r2);
        }

        private static bool PlayerIsAuthedOnBuilding(BasePlayer player, BuildingManager.Building building)
        {
            if (building.buildingPrivileges == null)
                return false;
            foreach (var p in building.buildingPrivileges)
            {
                if (p != null && !p.IsDestroyed && p.IsAuthed(player))
                    return true;
            }
            return false;
        }

        private static BuildingPrivlidge FindAnyCupboard(BuildingManager.Building building)
        {
            if (building.buildingPrivileges == null)
                return null;
            foreach (var p in building.buildingPrivileges)
            {
                if (p != null && !p.IsDestroyed)
                    return p;
            }
            return null;
        }

        // Lokales bounds → achsenparallele Welt-Bounds (WorldSpaceBounds() ist OBB, nicht Bounds).
        private static Bounds BlockWorldAxisAlignedBounds(BuildingBlock block)
        {
            var b = block.bounds;
            var m = block.transform.localToWorldMatrix;
            var c = b.center;
            var e = b.extents;
            var corners = new Vector3[8];
            var n = 0;
            for (var ix = 0; ix < 2; ix++)
            for (var iy = 0; iy < 2; iy++)
            for (var iz = 0; iz < 2; iz++)
            {
                corners[n++] = m.MultiplyPoint3x4(c + new Vector3(
                    ix == 0 ? -e.x : e.x,
                    iy == 0 ? -e.y : e.y,
                    iz == 0 ? -e.z : e.z));
            }

            var result = new Bounds(corners[0], Vector3.zero);
            for (var i = 1; i < 8; i++)
                result.Encapsulate(corners[i]);
            return result;
        }

        private static bool TryGetMergedBounds(BuildingManager.Building building, out Bounds merged)
        {
            merged = default;
            var first = true;
            if (building.buildingBlocks == null)
                return false;

            foreach (var block in building.buildingBlocks)
            {
                if (block == null || block.IsDestroyed)
                    continue;
                var box = BlockWorldAxisAlignedBounds(block);
                if (first)
                {
                    merged = box;
                    first = false;
                }
                else
                    merged.Encapsulate(box);
            }

            return !first;
        }

        private void SendSphere(BasePlayer player, Vector3 center, float radius)
        {
            var c = _config.SphereColorRgba;
            var col = new Color(
                c.Length > 0 ? c[0] : 0.4f,
                c.Length > 1 ? c[1] : 0.8f,
                c.Length > 2 ? c[2] : 0.4f,
                c.Length > 3 ? c[3] : 0.8f);

            player.SendConsoleCommand("ddraw.sphere", _config.DdrawDurationSeconds, col, center, radius);
        }

        private void Cleanup(ulong uid)
        {
            _buildingByPlayer.Remove(uid);
            if (_timers.TryGetValue(uid, out var t))
            {
                t?.Destroy();
                _timers.Remove(uid);
            }
        }
    }
}
